using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class PortCallActivityServiceTests
{
    private static readonly Guid ActorId = PortCallLogScenario.ActorId;

    private const string ActorName = PortCallLogScenario.ActorName;

    private static readonly DateTime ArrivalUtc = PortCallLogScenario.ActualArrivalUtc;

    [Fact]
    public async Task Recording_converts_an_explicit_offset_to_utc_and_writes_the_first_history_entry()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);

        var response = await PortCallLogScenario.RecordAsync(
            dbContext,
            voyageId,
            stopId,
            PortCallActivityType.AllFast,
            PortCallLogScenario.At(ArrivalUtc.AddMinutes(20), offsetHours: 2),
            PortCallLogScenario.At(ArrivalUtc.AddMinutes(35), offsetHours: -5),
            "  Lines ashore  ");

        var activity = await dbContext.Set<PortCallActivity>().SingleAsync();
        var revision = await dbContext.Set<PortCallActivityRevision>().SingleAsync();

        Assert.Equal(ArrivalUtc.AddMinutes(20), activity.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, activity.OccurredAtUtc.Kind);
        Assert.Equal(ArrivalUtc.AddMinutes(35), activity.EndedAtUtc);
        Assert.Equal("Lines ashore", activity.Notes);
        Assert.Equal(1, activity.Sequence);
        Assert.Equal(voyageId, activity.VoyageId);
        Assert.Equal(ActorId, activity.RecordedByUserId);
        Assert.Equal(PortCallLogScenario.UtcNow.UtcDateTime, activity.RecordedAtUtc);
        Assert.Equal(activity.ConcurrencyToken, response.Version);
        Assert.Equal(PortCallActivityRevision.RecordedChangeType, revision.ChangeType);
        Assert.Equal(ActorId, revision.ActorUserId);
        Assert.Equal(ActorName, revision.ActorName);
    }

    [Fact]
    public async Task Each_recorded_activity_gets_the_next_sequence_for_its_stop()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);

        var at = PortCallLogScenario.At(ArrivalUtc.AddHours(1));
        await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.CargoStarted, at);
        await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.OperationalNote, at);

        var sequences = await dbContext.Set<PortCallActivity>()
            .OrderBy(activity => activity.Sequence)
            .Select(activity => activity.Sequence)
            .ToListAsync();

        Assert.Equal([1, 2], sequences);
    }

    [Theory]
    [InlineData(null, "occurredAt")]
    [InlineData("Laytime", "activityType")]
    public async Task Missing_or_unknown_values_are_rejected(string? activityType, string expectedField)
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);

        var exception = await Assert.ThrowsAsync<PortCallLogValidationException>(() =>
            RecordService(dbContext).RecordAsync(
                voyageId,
                stopId,
                new RecordPortCallActivityRequest
                {
                    ActivityType = activityType ?? PortCallActivityType.AllFast,
                    OccurredAt = activityType is null ? null : PortCallLogScenario.At(ArrivalUtc),
                },
                ActorId,
                ActorName,
                CancellationToken.None));

        Assert.Contains(expectedField, exception.Errors.Keys);
    }

    [Fact]
    public async Task Future_start_and_end_times_are_rejected()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);
        var future = PortCallLogScenario.UtcNow.AddSeconds(1);

        var exception = await Assert.ThrowsAsync<PortCallLogValidationException>(() =>
            RecordService(dbContext).RecordAsync(
                voyageId,
                stopId,
                new RecordPortCallActivityRequest
                {
                    ActivityType = PortCallActivityType.AllFast,
                    OccurredAt = future,
                    EndedAt = future.AddHours(1),
                },
                ActorId,
                ActorName,
                CancellationToken.None));

        Assert.Contains(PortCallLogValidator.OccurredAtField, exception.Errors.Keys);
        Assert.Contains(PortCallLogValidator.EndedAtField, exception.Errors.Keys);
    }

    [Fact]
    public async Task An_occurrence_exactly_at_the_current_instant_is_accepted_even_with_a_non_utc_offset()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);

        await PortCallLogScenario.RecordAsync(
            dbContext,
            voyageId,
            stopId,
            PortCallActivityType.OperationalNote,
            PortCallLogScenario.UtcNow.ToOffset(TimeSpan.FromHours(9)));

        var activity = await dbContext.Set<PortCallActivity>().SingleAsync();
        Assert.Equal(PortCallLogScenario.UtcNow.UtcDateTime, activity.OccurredAtUtc);
    }

    [Fact]
    public async Task An_end_that_precedes_the_start_is_rejected_when_compared_in_utc()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);

        var exception = await Assert.ThrowsAsync<PortCallLogValidationException>(() =>
            RecordService(dbContext).RecordAsync(
                voyageId,
                stopId,
                new RecordPortCallActivityRequest
                {
                    ActivityType = PortCallActivityType.CargoStarted,
                    OccurredAt = PortCallLogScenario.At(ArrivalUtc.AddHours(2), offsetHours: 0),
                    EndedAt = PortCallLogScenario.At(ArrivalUtc.AddHours(1), offsetHours: 3),
                },
                ActorId,
                ActorName,
                CancellationToken.None));

        Assert.Equal(
            ["End time must not precede the occurrence time."],
            exception.Errors[PortCallLogValidator.EndedAtField]);
    }

    [Fact]
    public async Task Notes_longer_than_the_limit_are_rejected()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);

        var exception = await Assert.ThrowsAsync<PortCallLogValidationException>(() =>
            RecordService(dbContext).RecordAsync(
                voyageId,
                stopId,
                new RecordPortCallActivityRequest
                {
                    ActivityType = PortCallActivityType.OperationalNote,
                    OccurredAt = PortCallLogScenario.At(ArrivalUtc),
                    Notes = new string('x', PortCallActivity.NotesMaxLength + 1),
                },
                ActorId,
                ActorName,
                CancellationToken.None));

        Assert.Contains(PortCallLogValidator.NotesField, exception.Errors.Keys);
    }

    [Fact]
    public async Task A_stop_without_an_arrival_cannot_receive_activities()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, arrived: false);

        await Assert.ThrowsAsync<PortCallActivityStopNotArrivedException>(() =>
            PortCallLogScenario.RecordAsync(
                dbContext,
                voyageId,
                stopId,
                PortCallActivityType.PilotOnBoard,
                PortCallLogScenario.At(ArrivalUtc)));
    }

    [Fact]
    public async Task A_departed_stop_can_still_receive_activities_until_the_statement_is_finalized()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);

        await PortCallLogScenario.RecordAsync(
            dbContext,
            voyageId,
            stopId,
            PortCallActivityType.CargoCompleted,
            PortCallLogScenario.At(ArrivalUtc.AddHours(7)));

        Assert.Equal(1, await dbContext.Set<PortCallActivity>().CountAsync());
    }

    [Fact]
    public async Task A_stop_of_another_voyage_is_reported_as_not_found()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (_, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);
        var (otherVoyageId, _) = await PortCallLogScenario.SeedStopAsync(dbContext, voyageNumber: "VOY-LOG-101");

        await Assert.ThrowsAsync<VoyageStopNotFoundException>(() =>
            PortCallLogScenario.RecordAsync(
                dbContext,
                otherVoyageId,
                stopId,
                PortCallActivityType.AllFast,
                PortCallLogScenario.At(ArrivalUtc)));
        await Assert.ThrowsAsync<VoyageNotFoundException>(() =>
            PortCallLogScenario.RecordAsync(
                dbContext,
                Guid.NewGuid(),
                stopId,
                PortCallActivityType.AllFast,
                PortCallLogScenario.At(ArrivalUtc)));
    }

    [Fact]
    public async Task An_activity_of_another_stop_cannot_be_corrected_through_this_stop()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);
        var (otherVoyageId, otherStopId) = await PortCallLogScenario.SeedStopAsync(
            dbContext,
            voyageNumber: "VOY-LOG-102");
        var recorded = await PortCallLogScenario.RecordAsync(
            dbContext,
            otherVoyageId,
            otherStopId,
            PortCallActivityType.AllFast,
            PortCallLogScenario.At(ArrivalUtc));

        await Assert.ThrowsAsync<PortCallActivityNotFoundException>(() =>
            CorrectService(dbContext).CorrectAsync(
                voyageId,
                stopId,
                recorded.ActivityId,
                Correction(recorded.Version),
                ActorId,
                ActorName,
                CancellationToken.None));
    }

    [Fact]
    public async Task A_correction_keeps_every_earlier_version_with_its_reason_actor_and_time()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);
        var recorded = await PortCallLogScenario.RecordAsync(
            dbContext,
            voyageId,
            stopId,
            PortCallActivityType.AllFast,
            PortCallLogScenario.At(ArrivalUtc.AddMinutes(10)),
            notes: "Original");
        var correctorId = Guid.NewGuid();

        var corrected = await CorrectService(dbContext).CorrectAsync(
            voyageId,
            stopId,
            recorded.ActivityId,
            Correction(recorded.Version) with { Notes = "Corrected" },
            correctorId,
            "corrector@harborops.test",
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var details = await PortCallLogScenario.ActivitiesQuery(dbContext).GetActivityAsync(
            voyageId,
            stopId,
            recorded.ActivityId,
            CancellationToken.None);

        Assert.Equal(2, corrected.Revision);
        Assert.NotEqual(recorded.Version, corrected.Version);
        Assert.Equal("Corrected", details.Activity.Notes);
        Assert.Equal(PortCallActivityType.PilotOnBoard, details.Activity.ActivityType);
        Assert.Equal(correctorId, details.Activity.LastChangedByUserId);
        Assert.Equal([1, 2], details.History.Select(revision => revision.Revision));
        Assert.Equal("Original", details.History[0].Notes);
        Assert.Equal(PortCallActivityType.AllFast, details.History[0].ActivityType);
        Assert.Equal(ActorId, details.History[0].ActorUserId);
        Assert.Equal(ActorName, details.History[0].ActorName);
        Assert.Equal(PortCallActivityRevision.CorrectedChangeType, details.History[1].ChangeType);
        Assert.Equal("Pilot time was logged as all fast", details.History[1].Reason);
        Assert.Equal(correctorId, details.History[1].ActorUserId);
        Assert.Equal(PortCallLogScenario.UtcNow.UtcDateTime, details.History[1].ChangedAtUtc);
    }

    [Fact]
    public async Task Corrections_and_voids_require_a_reason_and_the_current_version()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);
        var recorded = await PortCallLogScenario.RecordAsync(
            dbContext,
            voyageId,
            stopId,
            PortCallActivityType.AllFast,
            PortCallLogScenario.At(ArrivalUtc));

        var correction = await Assert.ThrowsAsync<PortCallLogValidationException>(() =>
            CorrectService(dbContext).CorrectAsync(
                voyageId,
                stopId,
                recorded.ActivityId,
                Correction(null) with { Reason = "  " },
                ActorId,
                ActorName,
                CancellationToken.None));
        var voiding = await Assert.ThrowsAsync<PortCallLogValidationException>(() =>
            VoidService(dbContext).VoidAsync(
                voyageId,
                stopId,
                recorded.ActivityId,
                new VoidPortCallActivityRequest { Reason = new string('r', 201), Version = Guid.Empty },
                ActorId,
                ActorName,
                CancellationToken.None));

        Assert.Contains(PortCallLogValidator.ReasonField, correction.Errors.Keys);
        Assert.Contains(PortCallLogValidator.VersionField, correction.Errors.Keys);
        Assert.Contains(PortCallLogValidator.ReasonField, voiding.Errors.Keys);
        Assert.Contains(PortCallLogValidator.VersionField, voiding.Errors.Keys);
    }

    [Fact]
    public async Task A_change_based_on_a_stale_version_is_rejected_as_a_conflict()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);
        var recorded = await PortCallLogScenario.RecordAsync(
            dbContext,
            voyageId,
            stopId,
            PortCallActivityType.AllFast,
            PortCallLogScenario.At(ArrivalUtc));

        await CorrectService(dbContext).CorrectAsync(
            voyageId,
            stopId,
            recorded.ActivityId,
            Correction(recorded.Version),
            ActorId,
            ActorName,
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        await Assert.ThrowsAsync<PortCallLogConflictException>(() =>
            VoidService(dbContext).VoidAsync(
                voyageId,
                stopId,
                recorded.ActivityId,
                new VoidPortCallActivityRequest { Reason = "Duplicate", Version = recorded.Version },
                ActorId,
                ActorName,
                CancellationToken.None));

        var revisions = await dbContext.Set<PortCallActivityRevision>().CountAsync();
        Assert.Equal(2, revisions);
    }

    [Fact]
    public async Task Two_simultaneous_edits_of_the_same_activity_let_only_one_win()
    {
        var databaseName = $"PortCallLog-{Guid.NewGuid()}";
        await using var setup = PortCallLogScenario.CreateDbContext(databaseName);
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(setup);
        var recorded = await PortCallLogScenario.RecordAsync(
            setup,
            voyageId,
            stopId,
            PortCallActivityType.AllFast,
            PortCallLogScenario.At(ArrivalUtc));

        await using var first = PortCallLogScenario.CreateDbContext(databaseName);
        await using var second = PortCallLogScenario.CreateDbContext(databaseName);

        var firstActivity = await first.Set<PortCallActivity>().SingleAsync();
        var secondActivity = await second.Set<PortCallActivity>().SingleAsync();

        await first.Set<PortCallActivityRevision>().AddAsync(firstActivity.Void(
            "Duplicate",
            ActorId,
            ActorName,
            ArrivalUtc));
        await first.SaveChangesAsync();

        await second.Set<PortCallActivityRevision>().AddAsync(secondActivity.Correct(
            PortCallActivityType.PilotOnBoard,
            ArrivalUtc,
            null,
            null,
            "Wrong type",
            ActorId,
            ActorName,
            ArrivalUtc));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var verify = PortCallLogScenario.CreateDbContext(databaseName);
        var stored = await verify.Set<PortCallActivity>().SingleAsync();
        Assert.Equal(PortCallActivity.VoidedStatus, stored.Status);
        Assert.Equal(PortCallActivityType.AllFast, stored.ActivityType);
        Assert.NotEqual(recorded.Version, stored.ConcurrencyToken);
    }

    [Fact]
    public async Task A_voided_activity_leaves_the_timeline_but_stays_inspectable()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);
        var recorded = await PortCallLogScenario.RecordAsync(
            dbContext,
            voyageId,
            stopId,
            PortCallActivityType.AllFast,
            PortCallLogScenario.At(ArrivalUtc.AddMinutes(5)));

        var voided = await VoidService(dbContext).VoidAsync(
            voyageId,
            stopId,
            recorded.ActivityId,
            new VoidPortCallActivityRequest { Reason = "Entered twice", Version = recorded.Version },
            ActorId,
            ActorName,
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var timeline = await PortCallLogScenario.ActivitiesQuery(dbContext).GetTimelineAsync(
            voyageId,
            stopId,
            CancellationToken.None);
        var details = await PortCallLogScenario.ActivitiesQuery(dbContext).GetActivityAsync(
            voyageId,
            stopId,
            recorded.ActivityId,
            CancellationToken.None);

        Assert.Equal(PortCallActivity.VoidedStatus, voided.Status);
        Assert.DoesNotContain(timeline.Entries, entry => entry.SourceId == recorded.ActivityId);
        Assert.Contains(timeline.Activities, activity => activity.ActivityId == recorded.ActivityId);
        Assert.Equal(
            [PortCallActivityRevision.RecordedChangeType, PortCallActivityRevision.VoidedChangeType],
            details.History.Select(revision => revision.ChangeType));
        Assert.Equal("Entered twice", details.History[1].Reason);

        await Assert.ThrowsAsync<PortCallActivityVoidedException>(() =>
            CorrectService(dbContext).CorrectAsync(
                voyageId,
                stopId,
                recorded.ActivityId,
                Correction(voided.Version),
                ActorId,
                ActorName,
                CancellationToken.None));
    }

    [Fact]
    public async Task The_timeline_carries_the_port_time_zone_so_local_times_can_be_shown()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, timeZone: "Europe/Podgorica");

        var timeline = await PortCallLogScenario.ActivitiesQuery(dbContext).GetTimelineAsync(
            voyageId,
            stopId,
            CancellationToken.None);

        Assert.Equal("Europe/Podgorica", timeline.PortTimeZone);
        Assert.Equal("Berth 3", timeline.BerthName);
    }

    [Fact]
    public async Task A_save_rejected_by_the_database_is_reported_as_a_conflict()
    {
        var databaseName = $"PortCallLog-{Guid.NewGuid()}";
        await using (var setup = PortCallLogScenario.CreateDbContext(databaseName))
        {
            var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(setup);

            await using var failing = PortCallClearanceScenario.CreateDbContext(databaseName, failOnSave: true);

            await Assert.ThrowsAsync<PortCallLogConflictException>(() =>
                RecordService(failing).RecordAsync(
                    voyageId,
                    stopId,
                    new RecordPortCallActivityRequest
                    {
                        ActivityType = PortCallActivityType.AllFast,
                        OccurredAt = PortCallLogScenario.At(ArrivalUtc),
                    },
                    ActorId,
                    ActorName,
                    CancellationToken.None));
        }
    }

    [Fact]
    public async Task The_activity_list_is_paged_in_chronological_order_with_sequence_breaking_ties()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);
        var sameInstant = PortCallLogScenario.At(ArrivalUtc.AddHours(2));

        await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.CargoCompleted, sameInstant);
        await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.AllFast, PortCallLogScenario.At(ArrivalUtc));
        await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.OperationalNote, sameInstant.ToOffset(TimeSpan.FromHours(1)));

        var query = PortCallLogScenario.ActivitiesQuery(dbContext);
        var firstPage = await query.GetActivitiesAsync(
            voyageId,
            stopId,
            new GetPortCallActivitiesRequest { PageNumber = 1, PageSize = 2 },
            CancellationToken.None);
        var secondPage = await query.GetActivitiesAsync(
            voyageId,
            stopId,
            new GetPortCallActivitiesRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(
            [PortCallActivityType.AllFast, PortCallActivityType.CargoCompleted],
            firstPage.Items.Select(item => item.ActivityType));
        Assert.Equal([PortCallActivityType.OperationalNote], secondPage.Items.Select(item => item.ActivityType));
    }

    [Theory]
    [InlineData(0, 10, "pageNumber")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public async Task The_activity_list_rejects_unbounded_paging(int pageNumber, int pageSize, string field)
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);

        var exception = await Assert.ThrowsAsync<PortCallLogValidationException>(() =>
            PortCallLogScenario.ActivitiesQuery(dbContext).GetActivitiesAsync(
                voyageId,
                stopId,
                new GetPortCallActivitiesRequest { PageNumber = pageNumber, PageSize = pageSize },
                CancellationToken.None));

        Assert.Contains(field, exception.Errors.Keys);
    }

    [Fact]
    public async Task A_stop_cannot_hold_more_than_the_activity_limit()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);

        for (var sequence = 1; sequence <= PortCallActivity.MaxActivitiesPerStop; sequence++)
        {
            var (activity, _) = PortCallActivity.Record(
                voyageId,
                stopId,
                sequence,
                PortCallActivityType.OperationalNote,
                ArrivalUtc,
                null,
                null,
                ActorId,
                ActorName,
                ArrivalUtc);
            await dbContext.Set<PortCallActivity>().AddAsync(activity);
        }

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        await Assert.ThrowsAsync<PortCallActivityLimitReachedException>(() =>
            PortCallLogScenario.RecordAsync(
                dbContext,
                voyageId,
                stopId,
                PortCallActivityType.OperationalNote,
                PortCallLogScenario.At(ArrivalUtc)));
    }

    private static RecordPortCallActivityService RecordService(DbContext dbContext) =>
        new(dbContext, PortCallLogScenario.Clock());

    private static CorrectPortCallActivityService CorrectService(DbContext dbContext) =>
        new(dbContext, PortCallLogScenario.Clock());

    private static VoidPortCallActivityService VoidService(DbContext dbContext) =>
        new(dbContext, PortCallLogScenario.Clock());

    private static CorrectPortCallActivityRequest Correction(Guid? version) =>
        new()
        {
            ActivityType = PortCallActivityType.PilotOnBoard,
            OccurredAt = PortCallLogScenario.At(ArrivalUtc.AddMinutes(-15)),
            Reason = "Pilot time was logged as all fast",
            Version = version,
        };
}
