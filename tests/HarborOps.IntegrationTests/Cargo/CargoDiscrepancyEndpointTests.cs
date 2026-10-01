using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Cargo;

public sealed class CargoDiscrepancyEndpointTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";
    private const string Summary = "Seal replaced and verified against the manifest.";

    private static readonly Guid VoyageId = Guid.Parse("5e4d3c2b-1a09-4f8e-9d7c-6b5a4f3e2d1c");

    private static readonly DateTime ObservedAtUtc = DateTime.UtcNow.AddHours(-2);

    private readonly HarborOpsApiFactory _application;

    public CargoDiscrepancyEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task An_operator_investigates_and_resolves_a_discrepancy_and_the_booking_then_completes()
    {
        using var manager = await ManagerClientAsync();
        var (actorUserId, actorEmail) = await GetAuthenticatedAccountAsync(manager);
        var (bookingId, containerId) = await SeedBookingAsync(CargoStatus.Unloaded);

        using var registered = await manager.PostAsJsonAsync(
            DiscrepanciesEndpoint(bookingId),
            new
            {
                type = "SealMismatch",
                severity = "High",
                containerId,
                observedAtUtc = ObservedAtUtc,
                description = "Seal number differs from the booking",
                reportedByUserId = Guid.NewGuid(),
            },
            CancellationToken.None);
        var created = await registered.Content.ReadFromJsonAsync<CargoDiscrepancyDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(actorUserId, created.Discrepancy.ReportedByUserId);
        Assert.Equal(actorEmail, created.Discrepancy.ReportedByName);
        Assert.EndsWith($"/discrepancies/{created.Discrepancy.Id}", registered.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);

        var discrepancyId = created.Discrepancy.Id;

        using var blocked = await manager.PostAsJsonAsync(TrackingEventsEndpoint(bookingId), CompletionRequest(), CancellationToken.None);
        var blockedProblem = await blocked.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var blockers = blockedProblem.GetProperty("completionBlockers");

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("Cargo booking has unresolved discrepancies.", blockedProblem.GetProperty("title").GetString());
        Assert.Equal(1, blockers.GetProperty("openCount").GetInt32());
        Assert.Equal(1, blockers.GetProperty("reportedCount").GetInt32());
        Assert.Equal(0, blockers.GetProperty("investigatingCount").GetInt32());
        Assert.Equal(discrepancyId, blockers.GetProperty("discrepancies")[0].GetProperty("discrepancyId").GetGuid());

        await PostOkAsync(manager, $"{DiscrepancyEndpoint(bookingId, discrepancyId)}/investigation", new { });
        await PostOkAsync(manager, $"{DiscrepancyEndpoint(bookingId, discrepancyId)}/notes", new { note = "Seal photographed at berth 4." });
        var resolved = await PostOkAsync(
            manager,
            $"{DiscrepancyEndpoint(bookingId, discrepancyId)}/resolution",
            new { disposition = "Corrected", summary = Summary });

        Assert.Equal(nameof(CargoDiscrepancyStatus.Resolved), resolved.Discrepancy.Status);
        Assert.Equal(nameof(CargoDiscrepancyDisposition.Corrected), resolved.Discrepancy.Disposition);
        Assert.Equal(actorUserId, resolved.Discrepancy.ResolvedByUserId);
        Assert.Equal(actorEmail, resolved.Discrepancy.ResolvedByName);
        Assert.Equal(4, resolved.History.Count);
        Assert.All(resolved.History, entry => Assert.Equal(actorUserId, entry.ActorUserId));
        Assert.All(resolved.History, entry => Assert.Equal(actorEmail, entry.ActorName));

        using var completed = await manager.PostAsJsonAsync(TrackingEventsEndpoint(bookingId), CompletionRequest(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);
    }

    [Fact]
    public async Task The_completion_blockers_can_be_read_before_trying_to_complete()
    {
        using var manager = await ManagerClientAsync();
        var (bookingId, _) = await SeedBookingAsync(CargoStatus.Unloaded);
        var created = await RegisterAsync(manager, bookingId, "Shortage", containerId: null);
        using var reader = await ReaderClientAsync();

        var blockers = await reader.GetFromJsonAsync<CargoCompletionBlockers>(
            $"{DiscrepanciesEndpoint(bookingId)}/completion-blockers",
            CancellationToken.None);

        Assert.NotNull(blockers);
        Assert.Equal(1, blockers.OpenCount);
        Assert.Equal(created.Discrepancy.Id, Assert.Single(blockers.Discrepancies).DiscrepancyId);
    }

    [Fact]
    public async Task A_reader_can_list_and_open_discrepancies_but_not_change_them()
    {
        using var manager = await ManagerClientAsync();
        var (bookingId, containerId) = await SeedBookingAsync(CargoStatus.Arrived);
        var created = await RegisterAsync(manager, bookingId, "Damage", containerId);
        using var reader = await ReaderClientAsync();

        var list = await reader.GetFromJsonAsync<GetCargoDiscrepanciesResponse>(
            $"{DiscrepanciesEndpoint(bookingId)}?status=reported",
            CancellationToken.None);
        using var details = await reader.GetAsync(DiscrepancyEndpoint(bookingId, created.Discrepancy.Id), CancellationToken.None);
        using var register = await reader.PostAsJsonAsync(DiscrepanciesEndpoint(bookingId), ValidRegistration("Damage", containerId), CancellationToken.None);
        using var start = await reader.PostAsJsonAsync($"{DiscrepancyEndpoint(bookingId, created.Discrepancy.Id)}/investigation", new { }, CancellationToken.None);
        using var voiding = await reader.PostAsJsonAsync($"{DiscrepancyEndpoint(bookingId, created.Discrepancy.Id)}/void", new { reason = "No" }, CancellationToken.None);

        Assert.NotNull(list);
        Assert.Equal(created.Discrepancy.Id, Assert.Single(list.Items).Id);
        Assert.Equal(1, list.Summary.OpenCount);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, voiding.StatusCode);
        Assert.Equal(CargoDiscrepancyStatus.Reported, (await FindAsync(created.Discrepancy.Id)).Status);
    }

    [Fact]
    public async Task A_reader_cannot_complete_a_booking_through_the_api()
    {
        var (bookingId, _) = await SeedBookingAsync(CargoStatus.Unloaded);
        using var reader = await ReaderClientAsync();

        using var response = await reader.PostAsJsonAsync(TrackingEventsEndpoint(bookingId), CompletionRequest(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(CargoStatus.Unloaded, (await FindBookingAsync(bookingId)).Status);
    }

    [Fact]
    public async Task Callers_without_cargo_access_are_rejected()
    {
        var (bookingId, _) = await SeedBookingAsync(CargoStatus.Arrived);
        using var outsider = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.IdentityUsers.Read);
        using var anonymous = _application.CreateClient();

        using var forbidden = await outsider.GetAsync(DiscrepanciesEndpoint(bookingId), CancellationToken.None);
        using var unauthorized = await anonymous.GetAsync(DiscrepanciesEndpoint(bookingId), CancellationToken.None);
        using var unauthorizedPost = await anonymous.PostAsJsonAsync(DiscrepanciesEndpoint(bookingId), ValidRegistration("Shortage", null), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedPost.StatusCode);
    }

    [Fact]
    public async Task Registration_validates_the_request_and_container_ownership()
    {
        using var manager = await ManagerClientAsync();
        var (bookingId, _) = await SeedBookingAsync(CargoStatus.Arrived);
        var (_, foreignContainerId) = await SeedBookingAsync(CargoStatus.Arrived);

        using var invalid = await manager.PostAsJsonAsync(
            DiscrepanciesEndpoint(bookingId),
            new { type = "TemperatureRelated", severity = "Urgent", observedAtUtc = DateTime.UtcNow.AddHours(1), description = " " },
            CancellationToken.None);
        var invalidProblem = await invalid.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        using var foreign = await manager.PostAsJsonAsync(
            DiscrepanciesEndpoint(bookingId),
            ValidRegistration("Damage", foreignContainerId),
            CancellationToken.None);
        var foreignProblem = await foreign.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.NotNull(invalidProblem);
        Assert.Contains("containerId", invalidProblem.Errors.Keys);
        Assert.Contains("severity", invalidProblem.Errors.Keys);
        Assert.Contains("observedAtUtc", invalidProblem.Errors.Keys);
        Assert.Contains("description", invalidProblem.Errors.Keys);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.NotNull(foreignProblem);
        Assert.Contains("containerId", foreignProblem.Errors.Keys);
    }

    [Theory]
    [InlineData(nameof(CargoStatus.Completed), "Cargo booking is completed.")]
    [InlineData(nameof(CargoStatus.Planned), "Discrepancies cannot be registered yet.")]
    public async Task Registration_is_rejected_outside_the_cargo_movement(string status, string title)
    {
        using var manager = await ManagerClientAsync();
        var (bookingId, _) = await SeedBookingAsync(Enum.Parse<CargoStatus>(status));

        using var response = await manager.PostAsJsonAsync(DiscrepanciesEndpoint(bookingId), ValidRegistration("Shortage", null), CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(title, problem?.Title);
    }

    [Fact]
    public async Task Unknown_bookings_and_discrepancies_of_other_bookings_are_not_found()
    {
        using var manager = await ManagerClientAsync();
        var (bookingId, containerId) = await SeedBookingAsync(CargoStatus.Arrived);
        var (otherBookingId, _) = await SeedBookingAsync(CargoStatus.Arrived);
        var created = await RegisterAsync(manager, bookingId, "Damage", containerId);

        using var unknownBooking = await manager.GetAsync(DiscrepanciesEndpoint(Guid.NewGuid()), CancellationToken.None);
        using var crossBooking = await manager.GetAsync(DiscrepancyEndpoint(otherBookingId, created.Discrepancy.Id), CancellationToken.None);
        using var crossBookingVoid = await manager.PostAsJsonAsync(
            $"{DiscrepancyEndpoint(otherBookingId, created.Discrepancy.Id)}/void",
            new { reason = "Wrong booking" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, unknownBooking.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossBooking.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossBookingVoid.StatusCode);
        Assert.Equal(CargoDiscrepancyStatus.Reported, (await FindAsync(created.Discrepancy.Id)).Status);
    }

    [Fact]
    public async Task Lifecycle_violations_are_conflicts_and_missing_reasons_are_bad_requests()
    {
        using var manager = await ManagerClientAsync();
        var (bookingId, containerId) = await SeedBookingAsync(CargoStatus.Arrived);
        var created = await RegisterAsync(manager, bookingId, "Damage", containerId);
        var endpoint = DiscrepancyEndpoint(bookingId, created.Discrepancy.Id);

        using var earlyResolve = await manager.PostAsJsonAsync($"{endpoint}/resolution", new { disposition = "Corrected", summary = Summary }, CancellationToken.None);
        using var voidWithoutReason = await manager.PostAsJsonAsync($"{endpoint}/void", new { reason = "" }, CancellationToken.None);
        await PostOkAsync(manager, $"{endpoint}/void", new { reason = "Registered twice" });
        using var lateNote = await manager.PostAsJsonAsync($"{endpoint}/notes", new { note = "Too late" }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, earlyResolve.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, voidWithoutReason.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, lateNote.StatusCode);
    }

    [Fact]
    public async Task A_reefer_excursion_opens_one_discrepancy_that_waits_for_the_excursion()
    {
        using var manager = await ManagerClientAsync();
        var (bookingId, reeferId, excursionId) = await SeedExcursionAsync();
        var endpoint = $"/api/cargo/containers/{reeferId}/reefer/excursions/{excursionId}/discrepancy";

        using var first = await manager.PostAsJsonAsync(endpoint, new { severity = "High", description = "Warm cargo" }, CancellationToken.None);
        var created = await first.Content.ReadFromJsonAsync<OpenExcursionDiscrepancyResponse>(CancellationToken.None);
        using var second = await manager.PostAsJsonAsync(endpoint, new { }, CancellationToken.None);
        var reopened = await second.Content.ReadFromJsonAsync<OpenExcursionDiscrepancyResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotNull(created);
        Assert.NotNull(reopened);
        Assert.True(created.Created);
        Assert.False(reopened.Created);
        Assert.Equal(created.Details.Discrepancy.Id, reopened.Details.Discrepancy.Id);
        Assert.Equal(excursionId, created.Details.Source?.ExcursionId);

        var discrepancyId = created.Details.Discrepancy.Id;
        var bySource = await manager.GetFromJsonAsync<GetCargoDiscrepanciesResponse>(
            $"{DiscrepanciesEndpoint(bookingId)}?sourceExcursionId={excursionId}",
            CancellationToken.None);

        Assert.Equal(discrepancyId, Assert.Single(bySource!.Items).Id);

        await PostOkAsync(manager, $"{DiscrepancyEndpoint(bookingId, discrepancyId)}/investigation", new { });
        await PostOkAsync(manager, $"{DiscrepancyEndpoint(bookingId, discrepancyId)}/notes", new { note = "Reefer log checked." });

        using var waiting = await manager.PostAsJsonAsync(
            $"{DiscrepancyEndpoint(bookingId, discrepancyId)}/resolution",
            new { disposition = "AcceptedDeviation", summary = Summary },
            CancellationToken.None);
        var waitingProblem = await waiting.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, waiting.StatusCode);
        Assert.Equal("The linked temperature excursion is still open.", waitingProblem?.Title);
        Assert.Equal(ReeferExcursionStatus.Open, (await FindExcursionAsync(excursionId)).Status);
    }

    [Fact]
    public async Task A_superseded_excursion_cannot_open_a_discrepancy()
    {
        using var manager = await ManagerClientAsync();
        var (bookingId, reeferId, excursionId) = await SeedExcursionAsync(superseded: true);

        using var response = await manager.PostAsJsonAsync(
            $"/api/cargo/containers/{reeferId}/reefer/excursions/{excursionId}/discrepancy",
            new { severity = "High", description = "Warm cargo" },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);
        var list = await manager.GetFromJsonAsync<GetCargoDiscrepanciesResponse>(DiscrepanciesEndpoint(bookingId), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("The temperature excursion was superseded.", problem?.Title);
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task The_excursion_action_requires_cargo_manage()
    {
        var (_, reeferId, excursionId) = await SeedExcursionAsync();
        using var reader = await ReaderClientAsync();

        using var response = await reader.PostAsJsonAsync(
            $"/api/cargo/containers/{reeferId}/reefer/excursions/{excursionId}/discrepancy",
            new { severity = "High", description = "Warm cargo" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static object ValidRegistration(string type, Guid? containerId)
    {
        return new
        {
            type,
            severity = "Medium",
            containerId,
            observedAtUtc = ObservedAtUtc,
            description = "Observed at discharge",
        };
    }

    private static object CompletionRequest()
    {
        return new
        {
            status = nameof(CargoStatus.Completed),
            occurredUtc = DateTime.UtcNow,
            location = "Gate 2",
        };
    }

    private static async Task<CargoDiscrepancyDetails> RegisterAsync(
        HttpClient client,
        Guid bookingId,
        string type,
        Guid? containerId)
    {
        using var response = await client.PostAsJsonAsync(DiscrepanciesEndpoint(bookingId), ValidRegistration(type, containerId), CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<CargoDiscrepancyDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(details);

        return details;
    }

    private static async Task<CargoDiscrepancyDetails> PostOkAsync(HttpClient client, string uri, object body)
    {
        using var response = await client.PostAsJsonAsync(uri, body, CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<CargoDiscrepancyDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);

        return details;
    }

    private async Task<(Guid BookingId, Guid ContainerId)> SeedBookingAsync(CargoStatus status)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var booking = CargoBooking.Register(
            $"BK-DSC-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            VoyageId,
            "Frozen fish",
            "Reefer",
            12000m,
            20,
            isHazardous: false);

        while (booking.Status != status)
        {
            booking.AdvanceStatusTo(booking.Status + 1);
        }

        var container = Container.Register(
            $"DSCU{Random.Shared.Next(1_000_000, 9_999_999)}",
            "40HC",
            CargoStatus.Loaded,
            cargoBookingId: booking.Id);

        dbContext.Set<CargoBooking>().Add(booking);
        dbContext.Set<Container>().Add(container);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (booking.Id, container.Id);
    }

    private async Task<(Guid BookingId, Guid ReeferId, Guid ExcursionId)> SeedExcursionAsync(bool superseded = false)
    {
        var (bookingId, _) = await SeedBookingAsync(CargoStatus.InTransit);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var reefer = Container.Register(
            $"DSCR{Random.Shared.Next(1_000_000, 9_999_999)}",
            "42R1",
            CargoStatus.Loaded,
            cargoBookingId: bookingId);
        var profile = ReeferMonitoringProfile.Configure(reefer.Id, isReefer: true, -2m, 4m, 1m, Guid.NewGuid(), ObservedAtUtc.AddDays(-1));
        var reading = ReeferTemperatureReading.Record(profile, 7.5m, ObservedAtUtc, null, Guid.NewGuid(), ObservedAtUtc.AddMinutes(5));
        var excursion = ReeferExcursion.Open(reading);

        if (superseded)
        {
            excursion.Supersede(null, ObservedAtUtc.AddMinutes(30));
        }

        dbContext.Set<Container>().Add(reefer);
        dbContext.Set<ReeferMonitoringProfile>().Add(profile);
        dbContext.Set<ReeferTemperatureReading>().Add(reading);
        dbContext.Set<ReeferExcursion>().Add(excursion);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (bookingId, reefer.Id, excursion.Id);
    }

    private async Task<CargoDiscrepancy> FindAsync(Guid discrepancyId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<CargoDiscrepancy>().AsNoTracking().SingleAsync(candidate => candidate.Id == discrepancyId);
    }

    private async Task<CargoBooking> FindBookingAsync(Guid bookingId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<CargoBooking>().AsNoTracking().SingleAsync(candidate => candidate.Id == bookingId);
    }

    private async Task<ReeferExcursion> FindExcursionAsync(Guid excursionId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<ReeferExcursion>().AsNoTracking().SingleAsync(candidate => candidate.Id == excursionId);
    }

    private Task<HttpClient> ManagerClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
    }

    private Task<HttpClient> ReaderClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Read);
    }

    private static async Task<(Guid Id, string Email)> GetAuthenticatedAccountAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/identity/me", CancellationToken.None);
        var account = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (account.GetProperty("id").GetGuid(), account.GetProperty("email").GetString()!);
    }

    private static string DiscrepanciesEndpoint(Guid bookingId)
    {
        return $"{BookingsEndpoint}/{bookingId}/discrepancies";
    }

    private static string DiscrepancyEndpoint(Guid bookingId, Guid discrepancyId)
    {
        return $"{DiscrepanciesEndpoint(bookingId)}/{discrepancyId}";
    }

    private static string TrackingEventsEndpoint(Guid bookingId)
    {
        return $"{BookingsEndpoint}/{bookingId}/tracking-events";
    }
}
