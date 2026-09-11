using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class GetPortCallClearanceQueryTests
{
    private static readonly DateTime UtcNow = PortCallClearanceScenario.UtcNow;

    private static GetPortCallClearanceQuery CreateQuery(
        HarborOps.Infrastructure.Persistence.HarborOpsDbContext dbContext,
        DateTime? at = null) =>
        new(dbContext, new PortCallClearanceScenario.FixedTimeProvider(new DateTimeOffset(at ?? UtcNow)));

    [Fact]
    public async Task Items_are_ordered_by_required_first_then_due_time_category_and_item_id()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);

        await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Other,
            "Optional courtesy note",
            false);
        await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Security,
            "ISPS declaration",
            true,
            UtcNow.AddHours(8));
        await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Health,
            "Free pratique",
            true,
            UtcNow.AddHours(4));
        await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.PortAuthority,
            "Port authority clearance",
            true,
            UtcNow.AddHours(4));

        var response = await CreateQuery(dbContext)
            .GetAsync(voyageId, stopId, CancellationToken.None);

        Assert.Equal(
            ["Free pratique", "Port authority clearance", "ISPS declaration", "Optional courtesy note"],
            response.Items.Select(item => item.Title));
    }

    [Fact]
    public async Task Progress_counts_separate_required_optional_cleared_and_waived_items()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);

        var cleared = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true);
        var waived = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Health,
            "Free pratique",
            true);
        await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Security,
            "ISPS declaration",
            true);
        await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Other,
            "Optional courtesy note",
            false);

        clearance.ClearItem(cleared, "CUS-1", null, Guid.NewGuid(), UtcNow.AddMinutes(1));
        clearance.WaiveItem(waived, "Granted on arrival.", Guid.NewGuid(), UtcNow.AddMinutes(2));
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext)
            .GetAsync(voyageId, stopId, CancellationToken.None);

        Assert.Equal(4, response.Progress.TotalItemCount);
        Assert.Equal(3, response.Progress.RequiredItemCount);
        Assert.Equal(2, response.Progress.RequiredResolvedCount);
        Assert.Equal(1, response.Progress.RequiredPendingCount);
        Assert.Equal(1, response.Progress.OptionalItemCount);
        Assert.Equal(1, response.Progress.OptionalPendingCount);
        Assert.Equal(1, response.Progress.ClearedCount);
        Assert.Equal(1, response.Progress.WaivedCount);
        Assert.False(response.Progress.CanComplete);
    }

    [Fact]
    public async Task Progress_allows_completion_once_every_required_item_is_resolved()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        var required = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true);
        await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Other,
            "Optional courtesy note",
            false);

        clearance.ClearItem(required, null, null, Guid.NewGuid(), UtcNow.AddMinutes(1));
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext)
            .GetAsync(voyageId, stopId, CancellationToken.None);

        Assert.True(response.Progress.CanComplete);
        Assert.Equal(1, response.Progress.OptionalPendingCount);
    }

    [Fact]
    public async Task Items_expose_evidence_reference_and_decision_details()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        var evidenceDocumentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var dueAtUtc = UtcNow.AddHours(3);

        var cleared = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true,
            dueAtUtc);
        var waived = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Immigration,
            "Crew list",
            true,
            dueAtUtc);

        clearance.ClearItem(cleared, "CUS-42", evidenceDocumentId, actorId, UtcNow.AddMinutes(1));
        clearance.WaiveItem(waived, "Immigration waived the crew list.", actorId, UtcNow.AddMinutes(2));
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext)
            .GetAsync(voyageId, stopId, CancellationToken.None);

        var clearedItem = response.Items.Single(item => item.ItemId == cleared.Id);
        Assert.Equal("CUS-42", clearedItem.AuthorityReference);
        Assert.Equal(evidenceDocumentId, clearedItem.EvidenceDocumentId);
        Assert.Equal(dueAtUtc, clearedItem.DueAtUtc);
        Assert.Equal(actorId, clearedItem.DecidedByUserId);
        Assert.Equal(UtcNow.AddMinutes(1), clearedItem.DecidedAtUtc);
        Assert.Null(clearedItem.DecisionReason);

        var waivedItem = response.Items.Single(item => item.ItemId == waived.Id);
        Assert.Equal("Immigration waived the crew list.", waivedItem.DecisionReason);
        Assert.Null(waivedItem.EvidenceDocumentId);
    }

    [Fact]
    public async Task History_is_returned_in_append_order_with_actors_and_reasons()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        var actorId = Guid.NewGuid();
        var item = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true);

        var clearedEvent = clearance.ClearItem(item, null, null, actorId, UtcNow.AddMinutes(1));
        var completedEvent = clearance.Complete([item], actorId, UtcNow.AddMinutes(2));
        var reopenedEvent = clearance.Reopen("Customs raised a new query.", actorId, UtcNow.AddMinutes(3));
        await dbContext.Set<PortCallClearanceEvent>()
            .AddRangeAsync(clearedEvent, completedEvent, reopenedEvent);
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext)
            .GetAsync(voyageId, stopId, CancellationToken.None);

        Assert.Equal(
            [
                PortCallClearanceEvent.ItemAddedEventType,
                PortCallClearanceEvent.ItemClearedEventType,
                PortCallClearanceEvent.CompletedEventType,
                PortCallClearanceEvent.ReopenedEventType,
            ],
            response.History.Select(entry => entry.EventType));

        Assert.Equal(PortCallClearance.ReopenedStatus, response.Status);
        Assert.Null(response.CompletedByUserId);
        Assert.Equal(
            "Customs raised a new query.",
            response.History[^1].Reason);
        Assert.Equal(actorId, response.History[^1].ActorUserId);
        Assert.Equal(item.Id, response.History[1].ItemId);
        Assert.Null(response.History[2].ItemId);
    }

    [Fact]
    public async Task History_keeps_its_append_order_even_when_events_share_a_timestamp()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        var actorId = Guid.NewGuid();
        var item = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true);

        var clearedEvent = clearance.ClearItem(item, null, null, actorId, UtcNow);
        var completedEvent = clearance.Complete([item], actorId, UtcNow);
        var reopenedEvent = clearance.Reopen("Customs raised a new query.", actorId, UtcNow);
        await dbContext.Set<PortCallClearanceEvent>()
            .AddRangeAsync(clearedEvent, completedEvent, reopenedEvent);
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext)
            .GetAsync(voyageId, stopId, CancellationToken.None);

        Assert.Equal([1, 2, 3, 4], response.History.Select(entry => entry.Sequence));
        Assert.Equal(
            [
                PortCallClearanceEvent.ItemAddedEventType,
                PortCallClearanceEvent.ItemClearedEventType,
                PortCallClearanceEvent.CompletedEventType,
                PortCallClearanceEvent.ReopenedEventType,
            ],
            response.History.Select(entry => entry.EventType));
        Assert.Equal(4, clearance.LastEventSequence);
    }

    [Fact]
    public async Task Only_pending_required_items_past_their_due_time_count_as_overdue()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);

        var overdue = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Overdue required",
            true,
            UtcNow.AddHours(-2));
        var notDue = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Health,
            "Still in time",
            true,
            UtcNow.AddHours(2));
        var noDueDate = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Security,
            "No due time",
            true);
        var resolvedButLate = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Immigration,
            "Cleared after the due time",
            true,
            UtcNow.AddHours(-3));
        var optionalLate = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Other,
            "Optional and late",
            false,
            UtcNow.AddHours(-1));

        clearance.ClearItem(resolvedButLate, null, null, Guid.NewGuid(), UtcNow);
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext)
            .GetAsync(voyageId, stopId, CancellationToken.None);

        Assert.Equal(1, response.Progress.OverdueRequiredItemCount);
        Assert.True(response.Items.Single(item => item.ItemId == overdue.Id).IsOverdue);
        Assert.False(response.Items.Single(item => item.ItemId == notDue.Id).IsOverdue);
        Assert.False(response.Items.Single(item => item.ItemId == noDueDate.Id).IsOverdue);
        Assert.False(response.Items.Single(item => item.ItemId == resolvedButLate.Id).IsOverdue);
        Assert.False(response.Items.Single(item => item.ItemId == optionalLate.Id).IsOverdue);
    }

    [Fact]
    public async Task The_response_reports_the_server_time_the_due_states_were_evaluated_against()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var evaluatedAtUtc = UtcNow.AddHours(3);

        var response = await CreateQuery(dbContext, evaluatedAtUtc)
            .GetAsync(voyageId, stopId, CancellationToken.None);

        Assert.Equal(evaluatedAtUtc, response.EvaluatedAtUtc);
    }

    [Fact]
    public async Task An_unknown_voyage_stop_or_clearance_is_reported_as_not_found()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var query = CreateQuery(dbContext);

        await Assert.ThrowsAsync<VoyageNotFoundException>(
            () => query.GetAsync(Guid.NewGuid(), stopId, CancellationToken.None));

        await Assert.ThrowsAsync<VoyageStopNotFoundException>(
            () => query.GetAsync(voyageId, Guid.NewGuid(), CancellationToken.None));

        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        dbContext.Set<PortCallClearance>().Remove(clearance);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<PortCallClearanceNotFoundException>(
            () => query.GetAsync(voyageId, stopId, CancellationToken.None));
    }
}
