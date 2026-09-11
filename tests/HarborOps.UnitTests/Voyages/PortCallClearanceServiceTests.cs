using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class PortCallClearanceServiceTests
{
    private static readonly Guid ActorId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTime UtcNow = PortCallClearanceScenario.UtcNow;

    [Fact]
    public async Task Adding_an_item_stores_it_as_pending_with_an_append_only_event()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var evidenceDocumentId = Guid.NewGuid();
        var dueAtUtc = UtcNow.AddHours(6);

        var response = await AddService(dbContext).AddAsync(
            voyageId,
            stopId,
            new AddPortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Customs,
                Title = "  Customs declaration  ",
                IsRequired = true,
                DueAtUtc = dueAtUtc,
                AuthorityReference = "  CUS-42  ",
                EvidenceDocumentId = evidenceDocumentId,
            },
            ActorId,
            CancellationToken.None);

        var item = await dbContext.Set<PortCallClearanceItem>()
            .SingleAsync(candidate => candidate.Id == response.ItemId);

        Assert.Equal(PortCallClearanceItem.PendingStatus, item.Status);
        Assert.Equal("Customs declaration", item.Title);
        Assert.Equal("CUS-42", item.AuthorityReference);
        Assert.Equal(evidenceDocumentId, item.EvidenceDocumentId);
        Assert.Equal(dueAtUtc, item.DueAtUtc);
        Assert.True(item.IsRequired);
        Assert.Equal(ActorId, item.AddedByUserId);
        Assert.Equal(UtcNow, item.AddedAtUtc);

        var events = await PortCallClearanceScenario.GetEventsAsync(dbContext, response.ClearanceId);
        var recorded = Assert.Single(events);
        Assert.Equal(PortCallClearanceEvent.ItemAddedEventType, recorded.EventType);
        Assert.Equal(response.ItemId, recorded.PortCallClearanceItemId);
        Assert.Equal(ActorId, recorded.ActorUserId);
        Assert.Equal(UtcNow, recorded.OccurredAtUtc);
    }

    [Theory]
    [InlineData("Cargo", "Customs declaration")]
    [InlineData(PortCallClearanceCategory.Customs, "   ")]
    public async Task Adding_an_item_rejects_an_invalid_category_or_title(string category, string title)
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);

        await Assert.ThrowsAsync<PortCallClearanceValidationException>(
            () => AddService(dbContext).AddAsync(
                voyageId,
                stopId,
                new AddPortCallClearanceItemRequest { Category = category, Title = title },
                ActorId,
                CancellationToken.None));

        Assert.Empty(await dbContext.Set<PortCallClearanceItem>().ToListAsync());
        Assert.Empty(await dbContext.Set<PortCallClearanceEvent>().ToListAsync());
    }

    [Fact]
    public async Task Adding_an_item_rejects_a_due_time_that_is_not_utc()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);

        var exception = await Assert.ThrowsAsync<PortCallClearanceValidationException>(
            () => AddService(dbContext).AddAsync(
                voyageId,
                stopId,
                new AddPortCallClearanceItemRequest
                {
                    Category = PortCallClearanceCategory.Health,
                    Title = "Maritime declaration of health",
                    DueAtUtc = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Local),
                },
                ActorId,
                CancellationToken.None));

        Assert.Contains(PortCallClearanceValidator.DueAtUtcField, exception.Errors);
    }

    [Fact]
    public async Task Adding_an_item_is_rejected_once_the_stop_has_departed()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        await PortCallClearanceScenario.DepartStopAsync(dbContext, stopId);

        await Assert.ThrowsAsync<VoyageStopAlreadyDepartedException>(
            () => AddService(dbContext).AddAsync(
                voyageId,
                stopId,
                new AddPortCallClearanceItemRequest
                {
                    Category = PortCallClearanceCategory.Security,
                    Title = "ISPS declaration",
                },
                ActorId,
                CancellationToken.None));

        Assert.Empty(await dbContext.Set<PortCallClearanceItem>().ToListAsync());
    }

    [Fact]
    public async Task Adding_an_item_rejects_an_unknown_voyage_or_stop()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, _) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var request = new AddPortCallClearanceItemRequest
        {
            Category = PortCallClearanceCategory.Other,
            Title = "Ship sanitation certificate",
        };

        await Assert.ThrowsAsync<VoyageNotFoundException>(
            () => AddService(dbContext).AddAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                request,
                ActorId,
                CancellationToken.None));

        await Assert.ThrowsAsync<VoyageStopNotFoundException>(
            () => AddService(dbContext).AddAsync(
                voyageId,
                Guid.NewGuid(),
                request,
                ActorId,
                CancellationToken.None));
    }

    [Fact]
    public async Task Editing_a_pending_item_updates_it_and_records_an_event()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var item = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true);

        await UpdateService(dbContext, UtcNow.AddMinutes(1)).UpdateAsync(
            voyageId,
            stopId,
            item.Id,
            new UpdatePortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Immigration,
                Title = "Crew list",
                IsRequired = false,
                AuthorityReference = "IMM-9",
            },
            ActorId,
            CancellationToken.None);

        var persisted = await dbContext.Set<PortCallClearanceItem>()
            .SingleAsync(candidate => candidate.Id == item.Id);

        Assert.Equal(PortCallClearanceCategory.Immigration, persisted.Category);
        Assert.Equal("Crew list", persisted.Title);
        Assert.False(persisted.IsRequired);
        Assert.Equal("IMM-9", persisted.AuthorityReference);
        Assert.Equal(item.PortCallClearanceId, persisted.PortCallClearanceId);

        var events = await PortCallClearanceScenario.GetEventsAsync(
            dbContext,
            item.PortCallClearanceId);
        Assert.Equal(
            [PortCallClearanceEvent.ItemAddedEventType, PortCallClearanceEvent.ItemUpdatedEventType],
            events.Select(recorded => recorded.EventType));
    }

    [Fact]
    public async Task Editing_a_cleared_item_is_rejected_and_leaves_history_untouched()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var item = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true);

        await ClearService(dbContext).ClearAsync(
            voyageId,
            stopId,
            item.Id,
            new ClearPortCallClearanceItemRequest(),
            ActorId,
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<PortCallClearanceItemNotPendingException>(
            () => UpdateService(dbContext).UpdateAsync(
                voyageId,
                stopId,
                item.Id,
                new UpdatePortCallClearanceItemRequest
                {
                    Category = PortCallClearanceCategory.Customs,
                    Title = "Rewritten history",
                    IsRequired = true,
                },
                ActorId,
                CancellationToken.None));

        Assert.Equal(PortCallClearanceItem.ClearedStatus, exception.CurrentStatus);

        var persisted = await dbContext.Set<PortCallClearanceItem>()
            .SingleAsync(candidate => candidate.Id == item.Id);
        Assert.Equal("Customs declaration", persisted.Title);

        var events = await PortCallClearanceScenario.GetEventsAsync(
            dbContext,
            item.PortCallClearanceId);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task Editing_an_item_that_belongs_to_another_stop_is_not_found()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var (otherVoyageId, otherStopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext, "VOY-CLR-101");
        var foreignItem = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            otherStopId,
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true);

        Assert.NotEqual(voyageId, otherVoyageId);

        await Assert.ThrowsAsync<PortCallClearanceItemNotFoundException>(
            () => UpdateService(dbContext).UpdateAsync(
                voyageId,
                stopId,
                foreignItem.Id,
                new UpdatePortCallClearanceItemRequest
                {
                    Category = PortCallClearanceCategory.Customs,
                    Title = "Stolen item",
                    IsRequired = true,
                },
                ActorId,
                CancellationToken.None));
    }

    [Fact]
    public async Task Clearing_an_item_records_the_actor_timestamp_reference_and_evidence()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var item = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.PortAuthority,
            "Port authority clearance",
            true);
        var evidenceDocumentId = Guid.NewGuid();

        var response = await ClearService(dbContext).ClearAsync(
            voyageId,
            stopId,
            item.Id,
            new ClearPortCallClearanceItemRequest
            {
                AuthorityReference = "PA-7",
                EvidenceDocumentId = evidenceDocumentId,
            },
            ActorId,
            CancellationToken.None);

        Assert.Equal(PortCallClearanceItem.ClearedStatus, response.Status);
        Assert.Equal(ActorId, response.DecidedByUserId);
        Assert.Equal(UtcNow, response.DecidedAtUtc);

        var persisted = await dbContext.Set<PortCallClearanceItem>()
            .SingleAsync(candidate => candidate.Id == item.Id);
        Assert.Equal("PA-7", persisted.AuthorityReference);
        Assert.Equal(evidenceDocumentId, persisted.EvidenceDocumentId);
        Assert.Null(persisted.DecisionReason);
    }

    [Fact]
    public async Task A_second_decision_on_the_same_item_is_rejected_as_a_conflict()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        await using var writer = PortCallClearanceScenario.CreateDbContext(databaseName);
        await using var competitor = PortCallClearanceScenario.CreateDbContext(databaseName);
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(writer);
        var item = await PortCallClearanceScenario.SeedItemAsync(
            writer,
            stopId,
            PortCallClearanceCategory.Health,
            "Free pratique",
            true);

        await ClearService(writer).ClearAsync(
            voyageId,
            stopId,
            item.Id,
            new ClearPortCallClearanceItemRequest(),
            ActorId,
            CancellationToken.None);

        await Assert.ThrowsAsync<PortCallClearanceItemNotPendingException>(
            () => WaiveService(competitor).WaiveAsync(
                voyageId,
                stopId,
                item.Id,
                new WaivePortCallClearanceItemRequest { Reason = "Health authority unavailable." },
                Guid.NewGuid(),
                CancellationToken.None));

        var persisted = await competitor.Set<PortCallClearanceItem>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == item.Id);
        Assert.Equal(PortCallClearanceItem.ClearedStatus, persisted.Status);

        var events = await PortCallClearanceScenario.GetEventsAsync(
            writer,
            item.PortCallClearanceId);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task Waiving_an_item_requires_a_reason()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var item = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Security,
            "ISPS declaration",
            true);

        var exception = await Assert.ThrowsAsync<PortCallClearanceValidationException>(
            () => WaiveService(dbContext).WaiveAsync(
                voyageId,
                stopId,
                item.Id,
                new WaivePortCallClearanceItemRequest { Reason = "   " },
                ActorId,
                CancellationToken.None));

        Assert.Contains(PortCallClearanceValidator.ReasonField, exception.Errors);

        var persisted = await dbContext.Set<PortCallClearanceItem>()
            .SingleAsync(candidate => candidate.Id == item.Id);
        Assert.Equal(PortCallClearanceItem.PendingStatus, persisted.Status);
    }

    [Fact]
    public async Task Waiving_an_item_records_the_reason_actor_and_timestamp()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var item = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Security,
            "ISPS declaration",
            true);

        var response = await WaiveService(dbContext).WaiveAsync(
            voyageId,
            stopId,
            item.Id,
            new WaivePortCallClearanceItemRequest { Reason = "  Waived by the port state.  " },
            ActorId,
            CancellationToken.None);

        Assert.Equal(PortCallClearanceItem.WaivedStatus, response.Status);

        var persisted = await dbContext.Set<PortCallClearanceItem>()
            .SingleAsync(candidate => candidate.Id == item.Id);
        Assert.Equal("Waived by the port state.", persisted.DecisionReason);
        Assert.Equal(ActorId, persisted.DecidedByUserId);
        Assert.Equal(UtcNow, persisted.DecidedAtUtc);

        var events = await PortCallClearanceScenario.GetEventsAsync(
            dbContext,
            item.PortCallClearanceId);
        Assert.Equal(
            "Waived by the port state.",
            events.Single(recorded =>
                recorded.EventType == PortCallClearanceEvent.ItemWaivedEventType).Reason);
    }

    [Fact]
    public async Task Waiving_an_item_is_rejected_once_the_stop_has_departed()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var item = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true);
        await ClearService(dbContext).ClearAsync(
            voyageId,
            stopId,
            item.Id,
            new ClearPortCallClearanceItemRequest(),
            ActorId,
            CancellationToken.None);
        await CompleteService(dbContext).CompleteAsync(
            voyageId,
            stopId,
            ActorId,
            CancellationToken.None);
        await PortCallClearanceScenario.DepartStopAsync(dbContext, stopId);

        var optionalItem = PortCallClearanceItem.Add(
            item.PortCallClearanceId,
            PortCallClearanceCategory.Other,
            "Late paperwork",
            false,
            null,
            null,
            null,
            ActorId,
            UtcNow);
        await dbContext.Set<PortCallClearanceItem>().AddAsync(optionalItem);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<VoyageStopAlreadyDepartedException>(
            () => WaiveService(dbContext).WaiveAsync(
                voyageId,
                stopId,
                optionalItem.Id,
                new WaivePortCallClearanceItemRequest { Reason = "Too late." },
                ActorId,
                CancellationToken.None));
    }

    [Fact]
    public async Task Completion_requires_at_least_one_required_item()
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

        var exception = await Assert.ThrowsAsync<PortCallClearanceNotCompletableException>(
            () => CompleteService(dbContext).CompleteAsync(
                voyageId,
                stopId,
                ActorId,
                CancellationToken.None));

        Assert.Equal(0, exception.RequiredItemCount);

        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        Assert.Equal(PortCallClearance.PendingStatus, clearance.Status);
    }

    [Fact]
    public async Task Completion_is_rejected_while_a_required_item_is_still_pending()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true);

        var exception = await Assert.ThrowsAsync<PortCallClearanceNotCompletableException>(
            () => CompleteService(dbContext).CompleteAsync(
                voyageId,
                stopId,
                ActorId,
                CancellationToken.None));

        Assert.Equal(1, exception.UnresolvedRequiredItemCount);
        Assert.Empty(await dbContext.Set<PortCallClearanceEvent>()
            .Where(recorded => recorded.EventType == PortCallClearanceEvent.CompletedEventType)
            .ToListAsync());
    }

    [Fact]
    public async Task Completion_succeeds_with_cleared_and_waived_items_and_ignores_pending_optional_items()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
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
            PortCallClearanceCategory.Other,
            "Optional courtesy note",
            false);

        await ClearService(dbContext).ClearAsync(
            voyageId,
            stopId,
            cleared.Id,
            new ClearPortCallClearanceItemRequest(),
            ActorId,
            CancellationToken.None);
        await WaiveService(dbContext).WaiveAsync(
            voyageId,
            stopId,
            waived.Id,
            new WaivePortCallClearanceItemRequest { Reason = "Granted on arrival." },
            ActorId,
            CancellationToken.None);

        var response = await CompleteService(dbContext).CompleteAsync(
            voyageId,
            stopId,
            ActorId,
            CancellationToken.None);

        Assert.Equal(PortCallClearance.CompletedStatus, response.Status);

        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        Assert.Equal(ActorId, clearance.CompletedByUserId);
        Assert.Equal(UtcNow, clearance.CompletedAtUtc);
        Assert.True(clearance.AllowsDeparture);
    }

    [Fact]
    public async Task Completing_an_already_completed_clearance_is_rejected()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await CompletedClearanceAsync(dbContext);

        await Assert.ThrowsAsync<PortCallClearanceAlreadyCompletedException>(
            () => CompleteService(dbContext).CompleteAsync(
                voyageId,
                stopId,
                ActorId,
                CancellationToken.None));

        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        var events = await PortCallClearanceScenario.GetEventsAsync(dbContext, clearance.Id);
        Assert.Single(
            events,
            recorded => recorded.EventType == PortCallClearanceEvent.CompletedEventType);
    }

    [Fact]
    public async Task Reopening_requires_a_reason()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await CompletedClearanceAsync(dbContext);

        var exception = await Assert.ThrowsAsync<PortCallClearanceValidationException>(
            () => ReopenService(dbContext).ReopenAsync(
                voyageId,
                stopId,
                new ReopenPortCallClearanceRequest { Reason = null },
                ActorId,
                CancellationToken.None));

        Assert.Contains(PortCallClearanceValidator.ReasonField, exception.Errors);

        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        Assert.Equal(PortCallClearance.CompletedStatus, clearance.Status);
    }

    [Fact]
    public async Task Reopening_a_completed_clearance_records_an_event_and_clears_the_completion()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await CompletedClearanceAsync(dbContext);

        var response = await ReopenService(dbContext).ReopenAsync(
            voyageId,
            stopId,
            new ReopenPortCallClearanceRequest { Reason = "  Customs raised a new query.  " },
            ActorId,
            CancellationToken.None);

        Assert.Equal(PortCallClearance.ReopenedStatus, response.Status);

        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        Assert.Null(clearance.CompletedByUserId);
        Assert.Null(clearance.CompletedAtUtc);
        Assert.False(clearance.AllowsDeparture);

        var events = await PortCallClearanceScenario.GetEventsAsync(dbContext, clearance.Id);
        var reopened = Assert.Single(
            events,
            recorded => recorded.EventType == PortCallClearanceEvent.ReopenedEventType);
        Assert.Equal("Customs raised a new query.", reopened.Reason);
    }

    [Fact]
    public async Task Reopening_a_clearance_that_is_not_completed_is_rejected()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);

        var exception = await Assert.ThrowsAsync<PortCallClearanceNotCompletedException>(
            () => ReopenService(dbContext).ReopenAsync(
                voyageId,
                stopId,
                new ReopenPortCallClearanceRequest { Reason = "No reason to reopen." },
                ActorId,
                CancellationToken.None));

        Assert.Equal(PortCallClearance.PendingStatus, exception.CurrentStatus);
    }

    [Fact]
    public async Task Reopening_is_rejected_once_the_stop_has_departed()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await CompletedClearanceAsync(dbContext);
        await PortCallClearanceScenario.DepartStopAsync(dbContext, stopId);

        await Assert.ThrowsAsync<VoyageStopAlreadyDepartedException>(
            () => ReopenService(dbContext).ReopenAsync(
                voyageId,
                stopId,
                new ReopenPortCallClearanceRequest { Reason = "Too late." },
                ActorId,
                CancellationToken.None));

        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        Assert.Equal(PortCallClearance.CompletedStatus, clearance.Status);
    }

    [Fact]
    public async Task A_reopened_clearance_can_be_completed_again_after_a_replacement_item_is_resolved()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await CompletedClearanceAsync(dbContext);

        await ReopenService(dbContext, UtcNow.AddMinutes(3)).ReopenAsync(
            voyageId,
            stopId,
            new ReopenPortCallClearanceRequest { Reason = "Customs raised a new query." },
            ActorId,
            CancellationToken.None);

        var replacement = await AddService(dbContext, UtcNow.AddMinutes(4)).AddAsync(
            voyageId,
            stopId,
            new AddPortCallClearanceItemRequest
            {
                Category = PortCallClearanceCategory.Customs,
                Title = "Amended customs declaration",
                IsRequired = true,
            },
            ActorId,
            CancellationToken.None);

        await ClearService(dbContext, UtcNow.AddMinutes(5)).ClearAsync(
            voyageId,
            stopId,
            replacement.ItemId,
            new ClearPortCallClearanceItemRequest { AuthorityReference = "CUS-99" },
            ActorId,
            CancellationToken.None);

        var response = await CompleteService(dbContext, UtcNow.AddMinutes(6)).CompleteAsync(
            voyageId,
            stopId,
            ActorId,
            CancellationToken.None);

        Assert.Equal(PortCallClearance.CompletedStatus, response.Status);

        var events = await PortCallClearanceScenario.GetEventsAsync(dbContext, response.ClearanceId);
        Assert.Equal(
            [
                PortCallClearanceEvent.ItemAddedEventType,
                PortCallClearanceEvent.ItemClearedEventType,
                PortCallClearanceEvent.CompletedEventType,
                PortCallClearanceEvent.ReopenedEventType,
                PortCallClearanceEvent.ItemAddedEventType,
                PortCallClearanceEvent.ItemClearedEventType,
                PortCallClearanceEvent.CompletedEventType,
            ],
            events.Select(recorded => recorded.EventType));
    }

    [Fact]
    public async Task Concurrent_decisions_on_the_same_clearance_leave_a_single_outcome()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid stopId;
        Guid customsItemId;
        Guid healthItemId;

        await using (var seedContext = PortCallClearanceScenario.CreateDbContext(databaseName))
        {
            (_, stopId) = await PortCallClearanceScenario
                .SeedArrivedStopWithClearanceAsync(seedContext);
            customsItemId = (await PortCallClearanceScenario.SeedItemAsync(
                seedContext,
                stopId,
                PortCallClearanceCategory.Customs,
                "Customs declaration",
                true)).Id;
            healthItemId = (await PortCallClearanceScenario.SeedItemAsync(
                seedContext,
                stopId,
                PortCallClearanceCategory.Health,
                "Maritime declaration of health",
                true)).Id;
        }

        await using var clearContext = PortCallClearanceScenario.CreateDbContext(databaseName);
        await using var waiveContext = PortCallClearanceScenario.CreateDbContext(databaseName);

        var clearClearance = await clearContext.Set<PortCallClearance>()
            .SingleAsync(clearance => clearance.VoyageStopId == stopId);
        var waiveClearance = await waiveContext.Set<PortCallClearance>()
            .SingleAsync(clearance => clearance.VoyageStopId == stopId);

        var customsItem = await clearContext.Set<PortCallClearanceItem>()
            .SingleAsync(item => item.Id == customsItemId);
        var healthItem = await waiveContext.Set<PortCallClearanceItem>()
            .SingleAsync(item => item.Id == healthItemId);

        var clearedEvent = clearClearance.ClearItem(
            customsItem,
            null,
            null,
            ActorId,
            UtcNow.AddMinutes(1));
        await clearContext.Set<PortCallClearanceEvent>().AddAsync(clearedEvent);
        await clearContext.SaveChangesAsync();

        var waivedEvent = waiveClearance.WaiveItem(
            healthItem,
            "Authority waived the inspection.",
            ActorId,
            UtcNow.AddMinutes(2));
        await waiveContext.Set<PortCallClearanceEvent>().AddAsync(waivedEvent);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => waiveContext.SaveChangesAsync());

        await using var verifyContext = PortCallClearanceScenario.CreateDbContext(databaseName);
        var storedItems = await verifyContext.Set<PortCallClearanceItem>()
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(
            PortCallClearanceItem.ClearedStatus,
            storedItems.Single(item => item.Id == customsItemId).Status);
        Assert.Equal(
            PortCallClearanceItem.PendingStatus,
            storedItems.Single(item => item.Id == healthItemId).Status);

        var stored = await PortCallClearanceScenario.GetClearanceAsync(verifyContext, stopId);

        Assert.Equal(PortCallClearance.PendingStatus, stored.Status);
    }

    [Fact]
    public async Task Adding_an_item_writes_nothing_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid voyageId;
        Guid stopId;

        await using (var seedContext = PortCallClearanceScenario.CreateDbContext(databaseName))
        {
            (voyageId, stopId) = await PortCallClearanceScenario
                .SeedArrivedStopWithClearanceAsync(seedContext);
            await PortCallClearanceScenario.SeedItemAsync(
                seedContext,
                stopId,
                PortCallClearanceCategory.Customs,
                "Customs declaration",
                true);
        }

        await using (var failingContext = PortCallClearanceScenario.CreateDbContext(
            databaseName,
            failOnSave: true))
        {
            var exception = await Assert.ThrowsAsync<PortCallClearanceConflictException>(
                () => AddService(failingContext, UtcNow.AddMinutes(2)).AddAsync(
                    voyageId,
                    stopId,
                    new AddPortCallClearanceItemRequest
                    {
                        Category = PortCallClearanceCategory.Security,
                        Title = "Ship security declaration",
                        IsRequired = true,
                    },
                    ActorId,
                    CancellationToken.None));

            Assert.IsType<DbUpdateException>(exception.InnerException);
        }

        await using var verifyContext = PortCallClearanceScenario.CreateDbContext(databaseName);
        var storedItem = Assert.Single(await verifyContext.Set<PortCallClearanceItem>()
            .AsNoTracking()
            .ToListAsync());

        Assert.Equal(PortCallClearanceCategory.Customs, storedItem.Category);

        var stored = await PortCallClearanceScenario.GetClearanceAsync(verifyContext, stopId);
        var events = await PortCallClearanceScenario.GetEventsAsync(verifyContext, stored.Id);

        Assert.Equal(
            PortCallClearanceEvent.ItemAddedEventType,
            Assert.Single(events).EventType);
    }

    [Fact]
    public async Task Adding_an_item_is_rejected_while_the_clearance_is_completed()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await CompletedClearanceAsync(dbContext);

        await Assert.ThrowsAsync<PortCallClearanceAlreadyCompletedException>(
            () => AddService(dbContext, UtcNow.AddMinutes(3)).AddAsync(
                voyageId,
                stopId,
                new AddPortCallClearanceItemRequest
                {
                    Category = PortCallClearanceCategory.Security,
                    Title = "Ship security declaration",
                    IsRequired = true,
                },
                ActorId,
                CancellationToken.None));

        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        var items = await dbContext.Set<PortCallClearanceItem>()
            .AsNoTracking()
            .Where(item => item.PortCallClearanceId == clearance.Id)
            .ToListAsync();

        Assert.Equal(PortCallClearance.CompletedStatus, clearance.Status);
        Assert.Single(items);
    }

    [Fact]
    public async Task Editing_an_item_is_rejected_while_the_clearance_is_completed()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var optionalItem = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Other,
            "Optional courtesy note",
            false);
        var requiredItem = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true);
        await ClearService(dbContext, UtcNow.AddMinutes(1)).ClearAsync(
            voyageId,
            stopId,
            requiredItem.Id,
            new ClearPortCallClearanceItemRequest(),
            ActorId,
            CancellationToken.None);
        await CompleteService(dbContext, UtcNow.AddMinutes(2)).CompleteAsync(
            voyageId,
            stopId,
            ActorId,
            CancellationToken.None);

        await Assert.ThrowsAsync<PortCallClearanceAlreadyCompletedException>(
            () => UpdateService(dbContext, UtcNow.AddMinutes(3)).UpdateAsync(
                voyageId,
                stopId,
                optionalItem.Id,
                new UpdatePortCallClearanceItemRequest
                {
                    Category = PortCallClearanceCategory.Other,
                    Title = "Optional courtesy note",
                    IsRequired = true,
                },
                ActorId,
                CancellationToken.None));

        var stored = await dbContext.Set<PortCallClearanceItem>()
            .AsNoTracking()
            .SingleAsync(item => item.Id == optionalItem.Id);
        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);
        var events = await PortCallClearanceScenario.GetEventsAsync(dbContext, clearance.Id);

        Assert.False(stored.IsRequired);
        Assert.Equal(PortCallClearance.CompletedStatus, clearance.Status);
        Assert.DoesNotContain(
            PortCallClearanceEvent.ItemUpdatedEventType,
            events.Select(recorded => recorded.EventType));
    }

    [Fact]
    public async Task Departure_reports_a_conflict_and_writes_nothing_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid voyageId;
        Guid stopId;

        await using (var seedContext = PortCallClearanceScenario.CreateDbContext(databaseName))
        {
            (voyageId, stopId) = await CompletedClearanceAsync(seedContext);
        }

        await using (var failingContext = PortCallClearanceScenario.CreateDbContext(
            databaseName,
            failOnSave: true))
        {
            var service = new RecordStopDepartureService(failingContext, Clock(UtcNow.AddMinutes(5)));

            var exception = await Assert.ThrowsAsync<PortCallClearanceConflictException>(
                () => service.RecordDepartureAsync(
                    voyageId,
                    stopId,
                    new RecordStopDepartureRequest
                    {
                        ActualDepartureUtc = PortCallClearanceScenario.PlannedDepartureUtc.AddHours(3),
                    },
                    CancellationToken.None));

            Assert.IsType<DbUpdateException>(exception.InnerException);
        }

        await using var verifyContext = PortCallClearanceScenario.CreateDbContext(databaseName);
        var stop = await verifyContext.Set<VoyageStop>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == stopId);

        Assert.Equal(VoyageStop.ArrivedStatus, stop.Status);
        Assert.Null(stop.Atd);
    }

    [Fact]
    public async Task Concurrent_departure_and_reopen_leave_a_single_outcome()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid voyageId;
        Guid stopId;

        await using (var seedContext = PortCallClearanceScenario.CreateDbContext(databaseName))
        {
            (voyageId, stopId) = await CompletedClearanceAsync(seedContext);
        }

        await using var departureContext = PortCallClearanceScenario.CreateDbContext(databaseName);
        await using var reopenContext = PortCallClearanceScenario.CreateDbContext(databaseName);

        await reopenContext.Set<PortCallClearance>()
            .SingleAsync(clearance => clearance.VoyageStopId == stopId);

        await PortCallClearanceScenario.DepartStopAsync(departureContext, stopId);

        await Assert.ThrowsAsync<VoyageStopAlreadyDepartedException>(
            () => ReopenService(reopenContext, UtcNow.AddMinutes(5)).ReopenAsync(
                voyageId,
                stopId,
                new ReopenPortCallClearanceRequest { Reason = "Authority reversed the clearance." },
                ActorId,
                CancellationToken.None));

        await using var verifyContext = PortCallClearanceScenario.CreateDbContext(databaseName);
        var stored = await PortCallClearanceScenario.GetClearanceAsync(verifyContext, stopId);

        Assert.Equal(PortCallClearance.CompletedStatus, stored.Status);

        var events = await PortCallClearanceScenario.GetEventsAsync(verifyContext, stored.Id);

        Assert.DoesNotContain(
            PortCallClearanceEvent.ReopenedEventType,
            events.Select(recorded => recorded.EventType));
    }

    [Fact]
    public async Task A_reopen_read_before_the_departure_cannot_commit_after_the_stop_departs()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid voyageId;
        Guid stopId;

        await using (var seedContext = PortCallClearanceScenario.CreateDbContext(databaseName))
        {
            (voyageId, stopId) = await CompletedClearanceAsync(seedContext);
        }

        await using var reopenContext = PortCallClearanceScenario.CreateDbContext(databaseName);
        await using var departureContext = PortCallClearanceScenario.CreateDbContext(databaseName);

        var reopenClearance = await PortCallClearanceScenario.GetClearanceAsync(reopenContext, stopId);
        var reopenedEvent = reopenClearance.Reopen(
            "Authority reversed the clearance.",
            ActorId,
            UtcNow.AddMinutes(5));
        await reopenContext.Set<PortCallClearanceEvent>().AddAsync(reopenedEvent);

        await new RecordStopDepartureService(departureContext, Clock(UtcNow.AddMinutes(5)))
            .RecordDepartureAsync(
                voyageId,
                stopId,
                new RecordStopDepartureRequest
                {
                    ActualDepartureUtc = PortCallClearanceScenario.PlannedDepartureUtc.AddHours(3),
                },
                CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => reopenContext.SaveChangesAsync());

        await using var verifyContext = PortCallClearanceScenario.CreateDbContext(databaseName);
        var stop = await verifyContext.Set<VoyageStop>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == stopId);
        var stored = await PortCallClearanceScenario.GetClearanceAsync(verifyContext, stopId);

        Assert.Equal(VoyageStop.DepartedStatus, stop.Status);
        Assert.Equal(PortCallClearance.CompletedStatus, stored.Status);
    }

    [Fact]
    public async Task A_departure_read_before_a_reopen_cannot_commit_after_the_clearance_reopens()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid voyageId;
        Guid stopId;

        await using (var seedContext = PortCallClearanceScenario.CreateDbContext(databaseName))
        {
            (voyageId, stopId) = await CompletedClearanceAsync(seedContext);
        }

        await using var departureContext = PortCallClearanceScenario.CreateDbContext(databaseName);
        await using var reopenContext = PortCallClearanceScenario.CreateDbContext(databaseName);

        var departingStop = await departureContext.Set<VoyageStop>()
            .SingleAsync(candidate => candidate.Id == stopId);
        var departureClearance = await PortCallClearanceScenario.GetClearanceAsync(
            departureContext,
            stopId);
        departingStop.RecordDeparture(departingStop.Ata!.Value.AddHours(1));
        departureClearance.ConfirmDeparture(1, 0);

        await ReopenService(reopenContext, UtcNow.AddMinutes(5)).ReopenAsync(
            voyageId,
            stopId,
            new ReopenPortCallClearanceRequest { Reason = "Authority reversed the clearance." },
            ActorId,
            CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => departureContext.SaveChangesAsync());

        await using var verifyContext = PortCallClearanceScenario.CreateDbContext(databaseName);
        var stored = await PortCallClearanceScenario.GetClearanceAsync(verifyContext, stopId);

        Assert.Equal(PortCallClearance.ReopenedStatus, stored.Status);
    }

    private static async Task<(Guid VoyageId, Guid StopId)> CompletedClearanceAsync(
        HarborOpsDbContext dbContext)
    {
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var item = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true);

        await ClearService(dbContext, UtcNow.AddMinutes(1)).ClearAsync(
            voyageId,
            stopId,
            item.Id,
            new ClearPortCallClearanceItemRequest(),
            ActorId,
            CancellationToken.None);
        await CompleteService(dbContext, UtcNow.AddMinutes(2)).CompleteAsync(
            voyageId,
            stopId,
            ActorId,
            CancellationToken.None);

        return (voyageId, stopId);
    }

    private static AddPortCallClearanceItemService AddService(HarborOpsDbContext dbContext, DateTime? at = null) =>
        new(dbContext, Clock(at));

    private static UpdatePortCallClearanceItemService UpdateService(HarborOpsDbContext dbContext, DateTime? at = null) =>
        new(dbContext, Clock(at));

    private static ClearPortCallClearanceItemService ClearService(HarborOpsDbContext dbContext, DateTime? at = null) =>
        new(dbContext, Clock(at));

    private static WaivePortCallClearanceItemService WaiveService(HarborOpsDbContext dbContext, DateTime? at = null) =>
        new(dbContext, Clock(at));

    private static CompletePortCallClearanceService CompleteService(HarborOpsDbContext dbContext, DateTime? at = null) =>
        new(dbContext, Clock(at));

    private static ReopenPortCallClearanceService ReopenService(HarborOpsDbContext dbContext, DateTime? at = null) =>
        new(dbContext, Clock(at));

    private static TimeProvider Clock(DateTime? at) =>
        new PortCallClearanceScenario.FixedTimeProvider(new DateTimeOffset(at ?? UtcNow));
}
