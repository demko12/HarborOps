using System.Net;
using System.Net.Http.Json;
using HarborOps.Api.Composition.Voyages;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Notifications.Domain;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class VoyageDelayImpactEndpointTests : IntegrationTestBase
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    private readonly HarborOpsApiFactory _application;

    public VoyageDelayImpactEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_impact_preview_returns_the_proposed_downstream_estimates()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-100");
        var delay = await RecordDelayAsync(client, voyage.VoyageId, 120);

        using var response = await client.PostAsync(
            PreviewUri(voyage.VoyageId, delay),
            null,
            CancellationToken.None);
        var preview = await response.Content.ReadFromJsonAsync<DelayImpactPreviewResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(preview);
        Assert.Equal(120, preview.DelayMinutes);
        Assert.Equal(2, preview.Stops.Count);
        Assert.Equal(120, preview.PlannedArrivalDeltaMinutes);
        Assert.Equal(PlannedArrivalUtc.AddMinutes(120), preview.ProposedArrival);
        Assert.All(preview.Stops, stop => Assert.Equal(120, stop.EtaDeltaMinutes));
        Assert.All(
            preview.Stops,
            stop => Assert.Equal(stop.CurrentEta.AddMinutes(120), stop.ProposedEta));
    }

    [Fact]
    public async Task Post_impact_preview_persists_nothing()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-101");
        var delay = await RecordDelayAsync(client, voyage.VoyageId, 60);

        using var response = await client.PostAsync(
            PreviewUri(voyage.VoyageId, delay),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        Assert.Empty(await dbContext.Set<VoyageEstimateRevisionBatch>()
            .Where(batch => batch.VoyageId == voyage.VoyageId)
            .ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Post_impact_preview_returns_not_found_for_an_unknown_delay()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-102");

        using var response = await client.PostAsync(
            PreviewUri(voyage.VoyageId, Guid.NewGuid()),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_impact_preview_returns_conflict_for_a_cancelled_voyage()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-103");
        var delay = await RecordDelayAsync(client, voyage.VoyageId, 60);

        using var cancelResponse = await client.PostAsJsonAsync(
            new Uri($"/api/voyages/{voyage.VoyageId}/cancel", UriKind.Relative),
            new CancelVoyageRequest { Reason = "Charter withdrawn" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        using var response = await client.PostAsync(
            PreviewUri(voyage.VoyageId, delay),
            null,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("The voyage cannot be revised from its current status.", problem.Title);
    }

    [Fact]
    public async Task Post_impact_preview_without_the_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.PostAsync(
            PreviewUri(Guid.NewGuid(), Guid.NewGuid()),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_apply_impact_publishes_the_revision_and_the_operational_alert()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-110");
        var delay = await RecordDelayAsync(client, voyage.VoyageId, 90);
        var preview = await PreviewAsync(client, voyage.VoyageId, delay);

        using var response = await client.PostAsJsonAsync(
            ApplyUri(voyage.VoyageId, delay),
            ApplyRequest(preview, "Congestion in Bar"),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VoyageEstimateRevisionResult>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.True(result.AlertCreated);
        Assert.Equal(delay, result.DelayId);
        Assert.Equal("Congestion in Bar", result.Reason);
        Assert.NotEqual(Guid.Empty, result.RevisedByUserId);
        Assert.Equal(2, result.Changes.Count);
        Assert.All(
            result.Changes,
            change => Assert.Equal(90, (change.NewEta - change.PreviousEta).TotalMinutes));

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var stops = await dbContext.Set<VoyageStop>()
            .AsNoTracking()
            .Where(stop => stop.VoyageId == voyage.VoyageId)
            .OrderBy(stop => stop.StopOrder)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(voyage.FirstStopEta, stops[0].Eta);
        Assert.Equal(voyage.FirstStopEta.AddMinutes(90), stops[0].RevisedEta);

        var alert = await dbContext.Set<OperationAlert>()
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.SourceId == result.RevisionBatchId,
                CancellationToken.None);

        Assert.Equal(VoyageEstimateRevisionService.AlertCapability, alert.Capability);
        Assert.Equal(VoyageEstimateRevisionService.AlertSourceType, alert.SourceType);
    }

    [Fact]
    public async Task Post_apply_impact_revises_the_departure_of_the_stop_in_progress()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(
            client,
            "VOY-IMP-116",
            DateTime.UtcNow.AddMinutes(-5));
        var actualEventUtc = DateTime.UtcNow;

        using var departureResponse = await client.PostAsJsonAsync(
            new Uri($"/api/voyages/{voyage.VoyageId}/departure", UriKind.Relative),
            new RecordVoyageDepartureRequest { ActualDepartureUtc = actualEventUtc },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, departureResponse.StatusCode);

        var plannedStops = await ReadStopsAsync(voyage.VoyageId);

        using var arrivalResponse = await client.PostAsJsonAsync(
            new Uri(
                $"/api/voyages/{voyage.VoyageId}/stops/{plannedStops[0].Id}/arrival",
                UriKind.Relative),
            new RecordStopArrivalRequest { ActualArrivalUtc = actualEventUtc },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, arrivalResponse.StatusCode);

        var stops = await ReadStopsAsync(voyage.VoyageId);
        var delay = await RecordDelayAsync(client, voyage.VoyageId, 60);
        var preview = await PreviewAsync(client, voyage.VoyageId, delay);
        var currentStop = preview.Stops.Single(stop => stop.StopId == stops[0].Id);

        Assert.Equal(0, currentStop.EtaDeltaMinutes);
        Assert.Equal(60, currentStop.EtdDeltaMinutes);

        using var applyResponse = await client.PostAsJsonAsync(
            ApplyUri(voyage.VoyageId, delay),
            ApplyRequest(preview, "Loading delayed in port"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);

        var revised = await ReadStopsAsync(voyage.VoyageId);

        Assert.Equal(stops[0].Eta, revised[0].Eta);
        Assert.Equal(stops[0].Etd, revised[0].Etd);
        Assert.Equal(stops[0].Eta, revised[0].RevisedEta);
        Assert.Equal(stops[0].Etd.AddMinutes(60), revised[0].RevisedEtd);
        Assert.Equal(stops[0].Ata, revised[0].Ata);
        Assert.Null(revised[0].Atd);
    }

    [Fact]
    public async Task Post_apply_impact_returns_validation_problem_without_a_reason()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-111");
        var delay = await RecordDelayAsync(client, voyage.VoyageId, 30);
        var preview = await PreviewAsync(client, voyage.VoyageId, delay);

        using var response = await client.PostAsJsonAsync(
            ApplyUri(voyage.VoyageId, delay),
            ApplyRequest(preview, "  "),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(ApplyDelayImpactValidator.ReasonField, problem.Errors);
    }

    [Fact]
    public async Task Post_apply_impact_returns_not_found_for_an_unknown_voyage()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);

        using var response = await client.PostAsJsonAsync(
            ApplyUri(Guid.NewGuid(), Guid.NewGuid()),
            new ApplyDelayImpactRequest
            {
                Reason = "Downstream schedule revised",
                Stops =
                [
                    new ApplyDelayImpactStopRequest
                    {
                        VoyageStopId = Guid.NewGuid(),
                        ExpectedEta = PlannedDepartureUtc,
                        ExpectedEtd = PlannedDepartureUtc.AddHours(4),
                        Eta = PlannedDepartureUtc.AddHours(1),
                        Etd = PlannedDepartureUtc.AddHours(5),
                    },
                ],
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_apply_impact_returns_conflict_for_a_stale_preview()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-112");
        var staleDelay = await RecordDelayAsync(client, voyage.VoyageId, 60);
        var stalePreview = await PreviewAsync(client, voyage.VoyageId, staleDelay);

        var appliedDelay = await RecordDelayAsync(client, voyage.VoyageId, 15);
        var appliedPreview = await PreviewAsync(client, voyage.VoyageId, appliedDelay);

        using var firstResponse = await client.PostAsJsonAsync(
            ApplyUri(voyage.VoyageId, appliedDelay),
            ApplyRequest(appliedPreview),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using var response = await client.PostAsJsonAsync(
            ApplyUri(voyage.VoyageId, staleDelay),
            ApplyRequest(stalePreview),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("The impact preview is no longer current.", problem.Title);
    }

    [Fact]
    public async Task Post_apply_impact_returns_conflict_for_an_already_applied_delay()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-113");
        var delay = await RecordDelayAsync(client, voyage.VoyageId, 45);
        var preview = await PreviewAsync(client, voyage.VoyageId, delay);

        using var firstResponse = await client.PostAsJsonAsync(
            ApplyUri(voyage.VoyageId, delay),
            ApplyRequest(preview),
            CancellationToken.None);
        using var secondResponse = await client.PostAsJsonAsync(
            ApplyUri(voyage.VoyageId, delay),
            ApplyRequest(preview),
            CancellationToken.None);
        var problem = await secondResponse.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("The delay impact was already applied.", problem.Title);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        Assert.Single(await dbContext.Set<VoyageEstimateRevisionBatch>()
            .Where(batch => batch.VoyageId == voyage.VoyageId)
            .ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Post_apply_impact_returns_conflict_when_a_stop_was_executed_after_the_preview()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-114");
        var delay = await RecordDelayAsync(client, voyage.VoyageId, 60);
        var preview = await PreviewAsync(client, voyage.VoyageId, delay);

        using var departureResponse = await client.PostAsJsonAsync(
            new Uri($"/api/voyages/{voyage.VoyageId}/departure", UriKind.Relative),
            new RecordVoyageDepartureRequest
            {
                ActualDepartureUtc = DateTime.UtcNow,
            },
            CancellationToken.None);
        using var arrivalResponse = await client.PostAsJsonAsync(
            new Uri(
                $"/api/voyages/{voyage.VoyageId}/stops/{preview.Stops[0].StopId}/arrival",
                UriKind.Relative),
            new RecordStopArrivalRequest
            {
                ActualArrivalUtc = DateTime.UtcNow,
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, departureResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, arrivalResponse.StatusCode);

        using var response = await client.PostAsJsonAsync(
            ApplyUri(voyage.VoyageId, delay),
            ApplyRequest(preview),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("An executed voyage stop cannot be revised.", problem.Title);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        Assert.Empty(await dbContext.Set<VoyageEstimateRevisionBatch>()
            .Where(batch => batch.VoyageId == voyage.VoyageId)
            .ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Post_apply_impact_returns_conflict_when_the_estimates_break_the_chronology()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-115");
        var delay = await RecordDelayAsync(client, voyage.VoyageId, 60);
        var preview = await PreviewAsync(client, voyage.VoyageId, delay);
        var firstStop = preview.Stops[0];

        using var response = await client.PostAsJsonAsync(
            ApplyUri(voyage.VoyageId, delay),
            new ApplyDelayImpactRequest
            {
                Reason = "Downstream schedule revised",
                Stops =
                [
                    new ApplyDelayImpactStopRequest
                    {
                        VoyageStopId = firstStop.StopId,
                        ExpectedEta = firstStop.CurrentEta,
                        ExpectedEtd = firstStop.CurrentEtd,
                        Eta = firstStop.CurrentEta,
                        Etd = preview.Stops[1].CurrentEta.AddHours(2),
                    },
                ],
            },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(
            "The revised estimates do not preserve the voyage chronology.",
            problem.Title);
    }

    [Fact]
    public async Task Post_apply_impact_returns_validation_problem_without_any_stop()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-116");
        var delay = await RecordDelayAsync(client, voyage.VoyageId, 30);

        using var response = await client.PostAsJsonAsync(
            ApplyUri(voyage.VoyageId, delay),
            new ApplyDelayImpactRequest
            {
                Reason = "Downstream schedule revised",
                Stops = [],
            },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(ApplyDelayImpactValidator.StopsField, problem.Errors);
    }

    [Fact]
    public async Task Post_apply_impact_without_the_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.PostAsJsonAsync(
            ApplyUri(Guid.NewGuid(), Guid.NewGuid()),
            new ApplyDelayImpactRequest { Reason = "Downstream schedule revised" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_estimate_revisions_returns_the_current_estimates_and_the_history()
    {
        using var manageClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(manageClient, "VOY-IMP-120");
        var delay = await RecordDelayAsync(manageClient, voyage.VoyageId, 75);
        var preview = await PreviewAsync(manageClient, voyage.VoyageId, delay);

        using var applyResponse = await manageClient.PostAsJsonAsync(
            ApplyUri(voyage.VoyageId, delay),
            ApplyRequest(preview, "Weather routing"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await readClient.GetAsync(
            RevisionsUri(voyage.VoyageId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VoyageEstimateRevisionsResult>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(2, result.Stops.Count);
        Assert.Equal(voyage.FirstStopEta, result.Stops[0].PlannedEta);
        Assert.Equal(voyage.FirstStopEta.AddMinutes(75), result.Stops[0].RevisedEta);

        var revision = Assert.Single(result.Revisions);
        Assert.Equal("Weather routing", revision.Reason);
        Assert.True(revision.AlertCreated);
        Assert.Equal([1, 2], revision.Changes.Select(change => change.StopOrder));
    }

    [Fact]
    public async Task Get_estimate_revisions_returns_not_found_for_an_unknown_voyage()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.GetAsync(
            RevisionsUri(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_estimate_revisions_without_a_voyages_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(
            RevisionsUri(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_estimate_revisions_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            RevisionsUri(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_alert_retry_is_idempotent()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-130");
        var delay = await RecordDelayAsync(client, voyage.VoyageId, 30);
        var preview = await PreviewAsync(client, voyage.VoyageId, delay);

        using var applyResponse = await client.PostAsJsonAsync(
            ApplyUri(voyage.VoyageId, delay),
            ApplyRequest(preview),
            CancellationToken.None);
        var applied = await applyResponse.Content.ReadFromJsonAsync<VoyageEstimateRevisionResult>(
            CancellationToken.None);

        Assert.NotNull(applied);

        using var response = await client.PostAsync(
            AlertUri(voyage.VoyageId, applied.RevisionBatchId),
            null,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<EstimateRevisionAlertResult>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.True(result.AlertCreated);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        Assert.Single(await dbContext.Set<OperationAlert>()
            .Where(alert => alert.SourceId == applied.RevisionBatchId)
            .ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Post_alert_retry_returns_not_found_for_an_unknown_revision()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedVoyageWithItineraryAsync(client, "VOY-IMP-131");

        using var response = await client.PostAsync(
            AlertUri(voyage.VoyageId, Guid.NewGuid()),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_alert_retry_without_the_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.PostAsync(
            AlertUri(Guid.NewGuid(), Guid.NewGuid()),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static ApplyDelayImpactRequest ApplyRequest(
        DelayImpactPreviewResponse preview,
        string reason = "Downstream schedule revised")
    {
        return new ApplyDelayImpactRequest
        {
            Reason = reason,
            Stops = preview.Stops
                .Select(stop => new ApplyDelayImpactStopRequest
                {
                    VoyageStopId = stop.StopId,
                    ExpectedEta = stop.CurrentEta,
                    ExpectedEtd = stop.CurrentEtd,
                    Eta = stop.ProposedEta,
                    Etd = stop.ProposedEtd,
                })
                .ToList(),
        };
    }

    private static async Task<DelayImpactPreviewResponse> PreviewAsync(
        HttpClient client,
        Guid voyageId,
        Guid delayId)
    {
        using var response = await client.PostAsync(
            PreviewUri(voyageId, delayId),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var preview = await response.Content.ReadFromJsonAsync<DelayImpactPreviewResponse>(
            CancellationToken.None);

        Assert.NotNull(preview);

        return preview;
    }

    private static async Task<Guid> RecordDelayAsync(
        HttpClient client,
        Guid voyageId,
        int durationMinutes)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/voyages/{voyageId}/delays", UriKind.Relative),
            new RecordDelayRequest { Reason = "Port congestion", Duration = durationMinutes },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var delay = await response.Content.ReadFromJsonAsync<RecordDelayResponse>(
            CancellationToken.None);

        Assert.NotNull(delay);

        return delay.DelayId;
    }

    private static Uri PreviewUri(Guid voyageId, Guid delayId)
    {
        return new Uri(
            $"/api/voyages/{voyageId}/delays/{delayId}/impact-preview",
            UriKind.Relative);
    }

    private static Uri ApplyUri(Guid voyageId, Guid delayId)
    {
        return new Uri($"/api/voyages/{voyageId}/delays/{delayId}/apply-impact", UriKind.Relative);
    }

    private static Uri RevisionsUri(Guid voyageId)
    {
        return new Uri($"/api/voyages/{voyageId}/estimate-revisions", UriKind.Relative);
    }

    private static Uri AlertUri(Guid voyageId, Guid revisionBatchId)
    {
        return new Uri(
            $"/api/voyages/{voyageId}/estimate-revisions/{revisionBatchId}/alert",
            UriKind.Relative);
    }

    private async Task<List<VoyageStop>> ReadStopsAsync(Guid voyageId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<VoyageStop>()
            .AsNoTracking()
            .Where(stop => stop.VoyageId == voyageId)
            .OrderBy(stop => stop.StopOrder)
            .ToListAsync(CancellationToken.None);
    }

    private async Task<SeededVoyage> SeedVoyageWithItineraryAsync(
        HttpClient client,
        string voyageNumber,
        DateTime? itineraryStartUtc = null)
    {
        var departureUtc = itineraryStartUtc ?? PlannedDepartureUtc;
        var arrivalUtc = departureUtc.Add(PlannedArrivalUtc - PlannedDepartureUtc);
        Guid voyageId;
        Guid firstPortId;
        Guid secondPortId;

        await using (var scope = _application.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

            var voyage = Voyage.Schedule(
                Guid.NewGuid(),
                voyageNumber,
                "MERIJ",
                "ITGOA",
                departureUtc,
                arrivalUtc);
            var firstPort = Port.Create($"{voyageNumber} Bar", "Montenegro");
            var secondPort = Port.Create($"{voyageNumber} Kotor", "Montenegro");

            await dbContext.Set<Voyage>().AddAsync(voyage, CancellationToken.None);
            await dbContext.Set<Port>().AddRangeAsync([firstPort, secondPort], CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            voyageId = voyage.Id;
            firstPortId = firstPort.Id;
            secondPortId = secondPort.Id;
        }

        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/voyages/{voyageId}/itinerary", UriKind.Relative),
            new PlanVoyageItineraryRequest
            {
                Stops =
                [
                    new PlanVoyageItineraryStop
                    {
                        StopOrder = 1,
                        PortId = firstPortId,
                        DistanceFromPrevious = 0,
                        EstimatedDurationFromPrevious = 0,
                        Eta = departureUtc,
                        Etd = departureUtc.AddHours(4),
                    },
                    new PlanVoyageItineraryStop
                    {
                        StopOrder = 2,
                        PortId = secondPortId,
                        DistanceFromPrevious = 60m,
                        EstimatedDurationFromPrevious = 300,
                        Eta = departureUtc.AddHours(10),
                        Etd = departureUtc.AddHours(14),
                    },
                ],
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return new SeededVoyage(voyageId, departureUtc);
    }

    private sealed record SeededVoyage(Guid VoyageId, DateTime FirstStopEta);
}
