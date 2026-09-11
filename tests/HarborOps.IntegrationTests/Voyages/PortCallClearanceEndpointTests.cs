using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class PortCallClearanceEndpointTests : IntegrationTestBase
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    private readonly HarborOpsApiFactory _application;

    public PortCallClearanceEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Planning_an_itinerary_starts_every_new_stop_with_a_pending_clearance()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-200");

        using var readClient = await ReadClientAsync();

        foreach (var stopId in stopIds)
        {
            using var response = await readClient.GetAsync(
                ClearanceUri(voyageId, stopId),
                CancellationToken.None);
            var clearance = await response.Content
                .ReadFromJsonAsync<PortCallClearanceResponse>(CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(PortCallClearance.PendingStatus, clearance!.Status);
            Assert.Empty(clearance.Items);
            Assert.Empty(clearance.History);
            Assert.Equal(0, clearance.Progress.RequiredItemCount);
            Assert.False(clearance.Progress.CanComplete);
        }
    }

    [Fact]
    public async Task The_itinerary_exposes_the_clearance_summary_for_every_stop()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-201");
        await AddRequiredItemAsync(client, voyageId, stopIds[0], "Customs declaration");

        using var readClient = await ReadClientAsync();
        using var response = await readClient.GetAsync(
            new Uri($"/api/voyages/{voyageId}/itinerary", UriKind.Relative),
            CancellationToken.None);
        var itinerary = await response.Content
            .ReadFromJsonAsync<VoyageItineraryResponse>(CancellationToken.None);

        var first = itinerary!.Stops.Single(stop => stop.StopId == stopIds[0]);

        Assert.Equal(PortCallClearance.PendingStatus, first.ClearanceStatus);
        Assert.Equal(1, first.ClearanceRequiredItemCount);
        Assert.Equal(0, first.ClearanceRequiredResolvedCount);
    }

    [Fact]
    public async Task Reading_a_clearance_requires_the_voyages_read_capability()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-202");

        using var anonymousClient = _application.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync(
            ClearanceUri(voyageId, stopIds[0]),
            CancellationToken.None);

        using var unrelatedClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);
        using var unrelatedResponse = await unrelatedClient.GetAsync(
            ClearanceUri(voyageId, stopIds[0]),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unrelatedResponse.StatusCode);
    }

    [Fact]
    public async Task Changing_a_clearance_requires_the_voyages_manage_capability()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-203");

        using var readClient = await ReadClientAsync();
        using var response = await readClient.PostAsJsonAsync(
            ClearanceItemsUri(voyageId, stopIds[0]),
            new AddPortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Customs,
                Title = "Customs declaration",
                IsRequired = true,
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_voyages_stops_and_items_are_reported_as_not_found()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-204");

        using var unknownVoyage = await client.GetAsync(
            ClearanceUri(Guid.NewGuid(), stopIds[0]),
            CancellationToken.None);
        using var unknownStop = await client.GetAsync(
            ClearanceUri(voyageId, Guid.NewGuid()),
            CancellationToken.None);
        using var unknownItem = await client.PostAsJsonAsync(
            ClearanceItemDecisionUri(voyageId, stopIds[0], Guid.NewGuid(), "clear"),
            new ClearPortCallClearanceItemRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, unknownVoyage.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownStop.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownItem.StatusCode);
    }

    [Fact]
    public async Task An_invalid_category_text_or_date_is_rejected_with_validation_errors()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-205");

        using var invalidCategory = await client.PostAsJsonAsync(
            ClearanceItemsUri(voyageId, stopIds[0]),
            new AddPortCallClearanceItemRequest { Category = "Cargo", Title = "Customs declaration" },
            CancellationToken.None);

        using var invalidTitle = await client.PostAsJsonAsync(
            ClearanceItemsUri(voyageId, stopIds[0]),
            new AddPortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Customs,
                Title = "   ",
            },
            CancellationToken.None);

        using var invalidDueTime = await client.PostAsJsonAsync(
            ClearanceItemsUri(voyageId, stopIds[0]),
            new AddPortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Customs,
                Title = "Customs declaration",
                DueAtUtc = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified),
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, invalidCategory.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTitle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidDueTime.StatusCode);
    }

    [Fact]
    public async Task An_item_can_be_added_edited_and_then_cleared()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-206");
        var stopId = stopIds[0];
        var evidenceDocumentId = Guid.NewGuid();

        var itemId = await AddRequiredItemAsync(client, voyageId, stopId, "Customs declaration");

        using var updateResponse = await client.PutAsJsonAsync(
            ClearanceItemUri(voyageId, stopId, itemId),
            new UpdatePortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Customs,
                Title = "Amended customs declaration",
                IsRequired = true,
                AuthorityReference = "CUS-1",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        using var clearResponse = await client.PostAsJsonAsync(
            ClearanceItemDecisionUri(voyageId, stopId, itemId, "clear"),
            new ClearPortCallClearanceItemRequest
            {
                AuthorityReference = "CUS-2",
                EvidenceDocumentId = evidenceDocumentId,
            },
            CancellationToken.None);
        var decision = await clearResponse.Content
            .ReadFromJsonAsync<PortCallClearanceItemDecisionResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        Assert.Equal(PortCallClearanceItem.ClearedStatus, decision!.Status);

        using var clearanceResponse = await client.GetAsync(
            ClearanceUri(voyageId, stopId),
            CancellationToken.None);
        var clearance = await clearanceResponse.Content
            .ReadFromJsonAsync<PortCallClearanceResponse>(CancellationToken.None);

        var item = Assert.Single(clearance!.Items);
        Assert.Equal("Amended customs declaration", item.Title);
        Assert.Equal("CUS-2", item.AuthorityReference);
        Assert.Equal(evidenceDocumentId, item.EvidenceDocumentId);
        Assert.Equal(3, clearance.History.Count);
        Assert.True(clearance.Progress.CanComplete);
    }

    [Fact]
    public async Task A_cleared_item_can_no_longer_be_edited_cleared_or_waived()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-207");
        var stopId = stopIds[0];
        var itemId = await AddRequiredItemAsync(client, voyageId, stopId, "Customs declaration");

        using var firstClear = await client.PostAsJsonAsync(
            ClearanceItemDecisionUri(voyageId, stopId, itemId, "clear"),
            new ClearPortCallClearanceItemRequest(),
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, firstClear.StatusCode);

        using var repeatedClear = await client.PostAsJsonAsync(
            ClearanceItemDecisionUri(voyageId, stopId, itemId, "clear"),
            new ClearPortCallClearanceItemRequest(),
            CancellationToken.None);

        using var waiveAfterClear = await client.PostAsJsonAsync(
            ClearanceItemDecisionUri(voyageId, stopId, itemId, "waive"),
            new WaivePortCallClearanceItemRequest { Reason = "Changed my mind." },
            CancellationToken.None);

        using var editAfterClear = await client.PutAsJsonAsync(
            ClearanceItemUri(voyageId, stopId, itemId),
            new UpdatePortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Customs,
                Title = "Rewritten history",
                IsRequired = true,
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, repeatedClear.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, waiveAfterClear.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, editAfterClear.StatusCode);
    }

    [Fact]
    public async Task Waiving_an_item_requires_a_reason()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-208");
        var stopId = stopIds[0];
        var itemId = await AddRequiredItemAsync(client, voyageId, stopId, "Free pratique");

        using var missingReason = await client.PostAsJsonAsync(
            ClearanceItemDecisionUri(voyageId, stopId, itemId, "waive"),
            new WaivePortCallClearanceItemRequest(),
            CancellationToken.None);

        using var waived = await client.PostAsJsonAsync(
            ClearanceItemDecisionUri(voyageId, stopId, itemId, "waive"),
            new WaivePortCallClearanceItemRequest { Reason = "Health authority granted free pratique." },
            CancellationToken.None);
        var decision = await waived.Content
            .ReadFromJsonAsync<PortCallClearanceItemDecisionResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        Assert.Equal(HttpStatusCode.OK, waived.StatusCode);
        Assert.Equal(PortCallClearanceItem.WaivedStatus, decision!.Status);
    }

    [Fact]
    public async Task Completion_is_rejected_until_every_required_item_is_resolved()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-209");
        var stopId = stopIds[0];

        using var withoutItems = await client.PostAsJsonAsync(
            ClearanceCompleteUri(voyageId, stopId),
            new { },
            CancellationToken.None);

        var itemId = await AddRequiredItemAsync(client, voyageId, stopId, "Customs declaration");
        await AddOptionalItemAsync(client, voyageId, stopId, "Optional courtesy note");

        using var withPendingRequired = await client.PostAsJsonAsync(
            ClearanceCompleteUri(voyageId, stopId),
            new { },
            CancellationToken.None);

        await client.PostAsJsonAsync(
            ClearanceItemDecisionUri(voyageId, stopId, itemId, "clear"),
            new ClearPortCallClearanceItemRequest(),
            CancellationToken.None);

        using var completed = await client.PostAsJsonAsync(
            ClearanceCompleteUri(voyageId, stopId),
            new { },
            CancellationToken.None);
        var status = await completed.Content
            .ReadFromJsonAsync<PortCallClearanceStatusResponse>(CancellationToken.None);

        using var repeatedCompletion = await client.PostAsJsonAsync(
            ClearanceCompleteUri(voyageId, stopId),
            new { },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, withoutItems.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, withPendingRequired.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(PortCallClearance.CompletedStatus, status!.Status);
        Assert.Equal(HttpStatusCode.Conflict, repeatedCompletion.StatusCode);
    }

    [Fact]
    public async Task Reopening_requires_a_completed_clearance_and_a_reason()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-210");
        var stopId = stopIds[0];

        using var reopenWhilePending = await client.PostAsJsonAsync(
            ClearanceReopenUri(voyageId, stopId),
            new ReopenPortCallClearanceRequest { Reason = "Too early." },
            CancellationToken.None);

        await CompleteClearanceAsync(client, voyageId, stopId);

        using var reopenWithoutReason = await client.PostAsJsonAsync(
            ClearanceReopenUri(voyageId, stopId),
            new ReopenPortCallClearanceRequest(),
            CancellationToken.None);

        using var reopened = await client.PostAsJsonAsync(
            ClearanceReopenUri(voyageId, stopId),
            new ReopenPortCallClearanceRequest { Reason = "Customs raised a new query." },
            CancellationToken.None);
        var status = await reopened.Content
            .ReadFromJsonAsync<PortCallClearanceStatusResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, reopenWhilePending.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reopenWithoutReason.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.Equal(PortCallClearance.ReopenedStatus, status!.Status);
    }

    [Fact]
    public async Task Items_cannot_be_added_or_edited_while_the_clearance_is_completed()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-213");
        var stopId = stopIds[0];
        var optionalItemId = await AddOptionalItemAsync(
            client,
            voyageId,
            stopId,
            "Optional courtesy note");
        await CompleteClearanceAsync(client, voyageId, stopId);

        using var added = await client.PostAsJsonAsync(
            ClearanceItemsUri(voyageId, stopId),
            new AddPortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Security,
                Title = "Ship security declaration",
                IsRequired = true,
            },
            CancellationToken.None);

        using var edited = await client.PutAsJsonAsync(
            ClearanceItemUri(voyageId, stopId, optionalItemId),
            new UpdatePortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Other,
                Title = "Optional courtesy note",
                IsRequired = true,
            },
            CancellationToken.None);

        using var read = await client.GetAsync(ClearanceUri(voyageId, stopId), CancellationToken.None);
        var clearance = await read.Content
            .ReadFromJsonAsync<PortCallClearanceResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, added.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, edited.StatusCode);
        Assert.Equal(PortCallClearance.CompletedStatus, clearance!.Status);
        Assert.Equal(1, clearance.Progress.RequiredItemCount);
    }

    [Fact]
    public async Task The_departure_endpoint_returns_409_until_the_clearance_is_completed()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-211");
        var stopId = stopIds[0];

        await client.PostAsJsonAsync(
            new Uri($"/api/voyages/{voyageId}/departure", UriKind.Relative),
            new RecordVoyageDepartureRequest { ActualDepartureUtc = DateTime.UtcNow },
            CancellationToken.None);
        await client.PostAsJsonAsync(
            new Uri($"/api/voyages/{voyageId}/stops/{stopId}/arrival", UriKind.Relative),
            new RecordStopArrivalRequest { ActualArrivalUtc = DateTime.UtcNow },
            CancellationToken.None);

        using var blocked = await client.PostAsJsonAsync(
            StopDepartureUri(voyageId, stopId),
            new RecordStopDepartureRequest { ActualDepartureUtc = DateTime.UtcNow },
            CancellationToken.None);

        await CompleteClearanceAsync(client, voyageId, stopId);

        using var allowed = await client.PostAsJsonAsync(
            StopDepartureUri(voyageId, stopId),
            new RecordStopDepartureRequest { ActualDepartureUtc = DateTime.UtcNow },
            CancellationToken.None);
        var departure = await allowed.Content
            .ReadFromJsonAsync<RecordStopDepartureResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(VoyageStop.DepartedStatus, departure!.Status);
    }

    [Fact]
    public async Task Clearance_changes_are_rejected_once_the_stop_has_departed()
    {
        using var client = await ManageClientAsync();
        var (voyageId, stopIds) = await PlanItineraryAsync(client, "VOY-CLR-212");
        var stopId = stopIds[0];

        await client.PostAsJsonAsync(
            new Uri($"/api/voyages/{voyageId}/departure", UriKind.Relative),
            new RecordVoyageDepartureRequest { ActualDepartureUtc = DateTime.UtcNow },
            CancellationToken.None);
        await client.PostAsJsonAsync(
            new Uri($"/api/voyages/{voyageId}/stops/{stopId}/arrival", UriKind.Relative),
            new RecordStopArrivalRequest { ActualArrivalUtc = DateTime.UtcNow },
            CancellationToken.None);
        await CompleteClearanceAsync(client, voyageId, stopId);
        using var departure = await client.PostAsJsonAsync(
            StopDepartureUri(voyageId, stopId),
            new RecordStopDepartureRequest { ActualDepartureUtc = DateTime.UtcNow },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, departure.StatusCode);

        using var addAfterDeparture = await client.PostAsJsonAsync(
            ClearanceItemsUri(voyageId, stopId),
            new AddPortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Other,
                Title = "Late paperwork",
            },
            CancellationToken.None);

        using var reopenAfterDeparture = await client.PostAsJsonAsync(
            ClearanceReopenUri(voyageId, stopId),
            new ReopenPortCallClearanceRequest { Reason = "Too late." },
            CancellationToken.None);

        using var readAfterDeparture = await client.GetAsync(
            ClearanceUri(voyageId, stopId),
            CancellationToken.None);
        var clearance = await readAfterDeparture.Content
            .ReadFromJsonAsync<PortCallClearanceResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, addAfterDeparture.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reopenAfterDeparture.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readAfterDeparture.StatusCode);
        Assert.Equal(PortCallClearance.CompletedStatus, clearance!.Status);
    }

    private Task<HttpClient> ManageClientAsync() =>
        _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read,
            Permissions.Voyages.Manage);

    private Task<HttpClient> ReadClientAsync() =>
        _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Voyages.Read);

    private static Uri ClearanceUri(Guid voyageId, Guid stopId) =>
        new($"/api/voyages/{voyageId}/stops/{stopId}/clearance", UriKind.Relative);

    private static Uri ClearanceItemsUri(Guid voyageId, Guid stopId) =>
        new($"/api/voyages/{voyageId}/stops/{stopId}/clearance/items", UriKind.Relative);

    private static Uri ClearanceItemUri(Guid voyageId, Guid stopId, Guid itemId) =>
        new($"/api/voyages/{voyageId}/stops/{stopId}/clearance/items/{itemId}", UriKind.Relative);

    private static Uri ClearanceItemDecisionUri(
        Guid voyageId,
        Guid stopId,
        Guid itemId,
        string decision) =>
        new(
            $"/api/voyages/{voyageId}/stops/{stopId}/clearance/items/{itemId}/{decision}",
            UriKind.Relative);

    private static Uri ClearanceCompleteUri(Guid voyageId, Guid stopId) =>
        new($"/api/voyages/{voyageId}/stops/{stopId}/clearance/complete", UriKind.Relative);

    private static Uri ClearanceReopenUri(Guid voyageId, Guid stopId) =>
        new($"/api/voyages/{voyageId}/stops/{stopId}/clearance/reopen", UriKind.Relative);

    private static Uri StopDepartureUri(Guid voyageId, Guid stopId) =>
        new($"/api/voyages/{voyageId}/stops/{stopId}/departure", UriKind.Relative);

    private static async Task<Guid> AddRequiredItemAsync(
        HttpClient client,
        Guid voyageId,
        Guid stopId,
        string title)
    {
        using var response = await client.PostAsJsonAsync(
            ClearanceItemsUri(voyageId, stopId),
            new AddPortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Customs,
                Title = title,
                IsRequired = true,
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var added = await response.Content
            .ReadFromJsonAsync<AddPortCallClearanceItemResponse>(CancellationToken.None);

        return added!.ItemId;
    }

    private static async Task<Guid> AddOptionalItemAsync(
        HttpClient client,
        Guid voyageId,
        Guid stopId,
        string title)
    {
        using var response = await client.PostAsJsonAsync(
            ClearanceItemsUri(voyageId, stopId),
            new AddPortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Other,
                Title = title,
                IsRequired = false,
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var added = await response.Content
            .ReadFromJsonAsync<AddPortCallClearanceItemResponse>(CancellationToken.None);

        return added!.ItemId;
    }

    private static async Task CompleteClearanceAsync(
        HttpClient client,
        Guid voyageId,
        Guid stopId)
    {
        var itemId = await AddRequiredItemAsync(client, voyageId, stopId, "Customs declaration");

        using var clearResponse = await client.PostAsJsonAsync(
            ClearanceItemDecisionUri(voyageId, stopId, itemId, "clear"),
            new ClearPortCallClearanceItemRequest { AuthorityReference = "CUS-1" },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        using var completeResponse = await client.PostAsJsonAsync(
            ClearanceCompleteUri(voyageId, stopId),
            new { },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
    }

    private async Task<(Guid VoyageId, IReadOnlyList<Guid> StopIds)> PlanItineraryAsync(
        HttpClient client,
        string voyageNumber)
    {
        var voyageId = await SeedPlannedVoyageAsync(voyageNumber);
        var departurePortId = await SeedPortAsync($"Port of Bar {voyageNumber}");
        var arrivalPortId = await SeedPortAsync($"Port of Kotor {voyageNumber}");

        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/voyages/{voyageId}/itinerary", UriKind.Relative),
            new PlanVoyageItineraryRequest
            {
                Stops =
                [
                    new PlanVoyageItineraryStop
                    {
                        StopOrder = 1,
                        PortId = departurePortId,
                        DistanceFromPrevious = 0,
                        EstimatedDurationFromPrevious = 0,
                        Eta = PlannedDepartureUtc,
                        Etd = PlannedDepartureUtc.AddHours(4),
                    },
                    new PlanVoyageItineraryStop
                    {
                        StopOrder = 2,
                        PortId = arrivalPortId,
                        DistanceFromPrevious = 60m,
                        EstimatedDurationFromPrevious = 5,
                        Eta = PlannedDepartureUtc.AddHours(10),
                        Etd = PlannedDepartureUtc.AddHours(14),
                    },
                ],
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var plan = await response.Content
            .ReadFromJsonAsync<PlanVoyageItineraryResponse>(CancellationToken.None);

        return (voyageId, plan!.Stops.Select(stop => stop.StopId).ToList());
    }

    private async Task<Guid> SeedPlannedVoyageAsync(string voyageNumber)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MERIJ",
            "ITGOA",
            PlannedDepartureUtc,
            PlannedArrivalUtc);

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.SaveChangesAsync();

        return voyage.Id;
    }

    private async Task<Guid> SeedPortAsync(string name)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var port = Port.Create(name, "Montenegro");
        await dbContext.Set<Port>().AddAsync(port);
        await dbContext.SaveChangesAsync();

        return port.Id;
    }
}
