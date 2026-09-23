using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class PortCallStatementServiceTests
{
    private static readonly Guid ActorId = PortCallLogScenario.ActorId;

    private const string ActorName = PortCallLogScenario.ActorName;

    private static readonly DateTime ArrivalUtc = PortCallLogScenario.ActualArrivalUtc;

    private static readonly DateTime DepartureUtc = PortCallLogScenario.ActualDepartureUtc;

    [Fact]
    public async Task A_draft_previews_stop_times_delays_and_activities_in_deterministic_order()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);
        var sameInstant = ArrivalUtc.AddHours(2);

        await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.CargoStarted, PortCallLogScenario.At(sameInstant, 2));
        var delay = await PortCallLogScenario.SeedDelayAsync(dbContext, voyageId, stopId, "Crane breakdown", sameInstant);
        await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.AllFast, PortCallLogScenario.At(ArrivalUtc));
        await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.OperationalNote, PortCallLogScenario.At(sameInstant, -4));

        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);

        Assert.Equal(PortCallStatement.DraftStatus, preview.Status);
        Assert.True(preview.CanFinalize);
        Assert.Empty(preview.Blockers);
        Assert.Equal(64, preview.SourceVersion!.Length);
        Assert.Equal("VOY-LOG-100", preview.VoyageNumber);
        Assert.Equal("Port of Bar", preview.PortName);
        Assert.Equal("Berth 3", preview.BerthName);
        Assert.Equal(
            [
                (PortCallLogSource.StopArrival, PortCallLog.ActualArrivalLabel),
                (PortCallLogSource.ManualActivity, PortCallActivityType.AllFast),
                (PortCallLogSource.ManualActivity, PortCallActivityType.CargoStarted),
                (PortCallLogSource.ManualActivity, PortCallActivityType.OperationalNote),
                (PortCallLogSource.Delay, "Crane breakdown"),
                (PortCallLogSource.StopDeparture, PortCallLog.ActualDepartureLabel),
            ],
            preview.Entries.Select(entry => (entry.Source, entry.Label)));
        Assert.Equal([1, 2, 3, 4, 5, 6], preview.Entries.Select(entry => entry.Position));
        Assert.Equal(delay.Id, preview.Entries[4].SourceId);
        Assert.Equal(45, preview.Entries[4].DurationMinutes);
    }

    [Fact]
    public async Task Only_one_statement_can_exist_per_stop()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);
        await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);

        await Assert.ThrowsAsync<PortCallStatementAlreadyExistsException>(() =>
            PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId));
        Assert.Equal(1, await dbContext.Set<PortCallStatement>().CountAsync());
    }

    [Fact]
    public async Task An_arrived_stop_reports_the_missing_departure_as_blockers()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);

        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);

        Assert.False(preview.CanFinalize);
        Assert.Equal(
            [PortCallStatementBlocker.StopNotDepartedCode, PortCallStatementBlocker.ActualDepartureMissingCode],
            preview.Blockers.Select(blocker => blocker.Code));

        var exception = await Assert.ThrowsAsync<PortCallStatementNotFinalizableException>(() =>
            PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, preview));
        Assert.Equal(2, exception.Blockers.Count);
    }

    [Fact]
    public async Task A_planned_stop_reports_every_missing_prerequisite()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, arrived: false);

        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);

        Assert.Equal(
            [
                PortCallStatementBlocker.StopNotDepartedCode,
                PortCallStatementBlocker.ActualArrivalMissingCode,
                PortCallStatementBlocker.ActualDepartureMissingCode,
            ],
            preview.Blockers.Select(blocker => blocker.Code));
    }

    [Fact]
    public async Task An_uncorrected_activity_outside_the_port_call_blocks_finalization_until_it_is_corrected()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);
        var early = await PortCallLogScenario.RecordAsync(
            dbContext,
            voyageId,
            stopId,
            PortCallActivityType.PilotOnBoard,
            PortCallLogScenario.At(ArrivalUtc.AddMinutes(-40)));
        var late = await PortCallLogScenario.RecordAsync(
            dbContext,
            voyageId,
            stopId,
            PortCallActivityType.CargoCompleted,
            PortCallLogScenario.At(DepartureUtc.AddHours(-1)),
            PortCallLogScenario.At(DepartureUtc.AddMinutes(1)));
        await PortCallLogScenario.RecordAsync(
            dbContext,
            voyageId,
            stopId,
            PortCallActivityType.AllFast,
            PortCallLogScenario.At(ArrivalUtc));

        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);

        Assert.False(preview.CanFinalize);
        Assert.Equal([early.ActivityId, late.ActivityId], preview.Blockers.Select(blocker => blocker.ActivityId));
        Assert.All(
            preview.Blockers,
            blocker => Assert.Equal(PortCallStatementBlocker.ActivityOutsidePortCallCode, blocker.Code));
        Assert.True(preview.Entries.Single(entry => entry.SourceId == early.ActivityId).IsOutsidePortCall);

        await Assert.ThrowsAsync<PortCallStatementNotFinalizableException>(() =>
            PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, preview));

        await new CorrectPortCallActivityService(dbContext, PortCallLogScenario.Clock()).CorrectAsync(
            voyageId,
            stopId,
            early.ActivityId,
            new CorrectPortCallActivityRequest
            {
                ActivityType = PortCallActivityType.PilotOnBoard,
                OccurredAt = PortCallLogScenario.At(ArrivalUtc),
                Reason = "Pilot boarded at the berth",
                Version = early.Version,
            },
            ActorId,
            ActorName,
            CancellationToken.None);
        await new VoidPortCallActivityService(dbContext, PortCallLogScenario.Clock()).VoidAsync(
            voyageId,
            stopId,
            late.ActivityId,
            new VoidPortCallActivityRequest { Reason = "Logged on the next stop", Version = late.Version },
            ActorId,
            ActorName,
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var refreshed = await PortCallLogScenario.StatementQuery(dbContext).GetAsync(
            voyageId,
            stopId,
            CancellationToken.None);
        var finalized = await PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, refreshed);

        Assert.True(refreshed.CanFinalize);
        Assert.Equal(PortCallStatement.FinalizedStatus, finalized.Status);
    }

    [Fact]
    public async Task Finalizing_snapshots_sources_locks_activities_and_records_the_finalizer()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);
        await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.AllFast, PortCallLogScenario.At(ArrivalUtc.AddMinutes(15)));
        await PortCallLogScenario.SeedDelayAsync(dbContext, voyageId, stopId, "Crane breakdown", ArrivalUtc.AddHours(3));
        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);

        var finalized = await PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, preview);

        Assert.Equal(PortCallStatement.FinalizedStatus, finalized.Status);
        Assert.Equal(ActorId, finalized.FinalizedByUserId);
        Assert.Equal(ActorName, finalized.FinalizedByName);
        Assert.Equal(PortCallLogScenario.UtcNow.UtcDateTime, finalized.FinalizedAtUtc);
        Assert.Equal(ArrivalUtc, finalized.ActualArrivalUtc);
        Assert.Equal(DepartureUtc, finalized.ActualDepartureUtc);
        Assert.Equal(preview.SourceVersion, finalized.SourceVersion);
        Assert.False(finalized.CanFinalize);
        Assert.Equal(
            preview.Entries.Select(entry => (entry.Source, entry.Label, entry.OccurredAtUtc)),
            finalized.Entries.Select(entry => (entry.Source, entry.Label, entry.OccurredAtUtc)));
        Assert.Equal(4, await dbContext.Set<PortCallStatementEntry>().CountAsync());
        Assert.All(await dbContext.Set<PortCallActivity>().ToListAsync(), activity => Assert.True(activity.IsLocked));
    }

    [Fact]
    public async Task A_finalized_statement_cannot_be_finalized_again()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);
        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);
        await PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, preview);

        await Assert.ThrowsAsync<PortCallStatementAlreadyFinalizedException>(() =>
            PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, preview));
        Assert.Equal(2, await dbContext.Set<PortCallStatementEntry>().CountAsync());
    }

    [Fact]
    public async Task Activities_of_a_finalized_statement_cannot_be_recorded_corrected_or_voided()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);
        var recorded = await PortCallLogScenario.RecordAsync(
            dbContext,
            voyageId,
            stopId,
            PortCallActivityType.AllFast,
            PortCallLogScenario.At(ArrivalUtc.AddMinutes(15)));
        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);
        await PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, preview);
        var lockedVersion = (await dbContext.Set<PortCallActivity>().SingleAsync()).ConcurrencyToken;
        dbContext.ChangeTracker.Clear();

        await Assert.ThrowsAsync<PortCallStatementAlreadyFinalizedException>(() =>
            PortCallLogScenario.RecordAsync(
                dbContext,
                voyageId,
                stopId,
                PortCallActivityType.OperationalNote,
                PortCallLogScenario.At(ArrivalUtc.AddHours(1))));
        await Assert.ThrowsAsync<PortCallActivityLockedException>(() =>
            new CorrectPortCallActivityService(dbContext, PortCallLogScenario.Clock()).CorrectAsync(
                voyageId,
                stopId,
                recorded.ActivityId,
                new CorrectPortCallActivityRequest
                {
                    ActivityType = PortCallActivityType.AllFast,
                    OccurredAt = PortCallLogScenario.At(ArrivalUtc.AddMinutes(20)),
                    Reason = "Late fix",
                    Version = lockedVersion,
                },
                ActorId,
                ActorName,
                CancellationToken.None));
        await Assert.ThrowsAsync<PortCallActivityLockedException>(() =>
            new VoidPortCallActivityService(dbContext, PortCallLogScenario.Clock()).VoidAsync(
                voyageId,
                stopId,
                recorded.ActivityId,
                new VoidPortCallActivityRequest { Reason = "Late void", Version = lockedVersion },
                ActorId,
                ActorName,
                CancellationToken.None));

        var timeline = await PortCallLogScenario.ActivitiesQuery(dbContext).GetTimelineAsync(
            voyageId,
            stopId,
            CancellationToken.None);
        Assert.False(timeline.CanRecordActivities);
        Assert.Equal(PortCallStatement.FinalizedStatus, timeline.Statement!.Status);
    }

    [Fact]
    public async Task The_finalized_snapshot_ignores_later_changes_to_source_labels_and_delays()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true, timeZone: "Europe/Podgorica");
        await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.AllFast, PortCallLogScenario.At(ArrivalUtc.AddMinutes(15)));
        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);
        var finalized = await PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, preview);

        var port = await dbContext.Set<Port>().SingleAsync();
        dbContext.Entry(port).Property(candidate => candidate.Name).CurrentValue = "Luka Bar (renamed)";
        dbContext.Entry(port).Property(candidate => candidate.TimeZone).CurrentValue = "Europe/Belgrade";
        var berth = await dbContext.Set<Berth>().SingleAsync();
        dbContext.Entry(berth).Property(candidate => candidate.Name).CurrentValue = "Berth 9";
        await dbContext.SaveChangesAsync();
        await PortCallLogScenario.SeedDelayAsync(dbContext, voyageId, stopId, "Late paperwork", ArrivalUtc.AddHours(4));
        dbContext.ChangeTracker.Clear();

        var read = await PortCallLogScenario.StatementQuery(dbContext).GetAsync(voyageId, stopId, CancellationToken.None);
        var print = await PortCallLogScenario.StatementQuery(dbContext).GetPrintAsync(voyageId, stopId, CancellationToken.None);

        foreach (var statement in new[] { read, print })
        {
            Assert.Equal("Port of Bar", statement.PortName);
            Assert.Equal("Berth 3", statement.BerthName);
            Assert.Equal("Europe/Podgorica", statement.PortTimeZone);
            Assert.Equal("SOF-VOY-LOG-100-01", statement.DocumentNumber);
            Assert.Equal(ActorName, statement.FinalizedByName);
            Assert.Equal(finalized.Entries.Count, statement.Entries.Count);
            Assert.DoesNotContain(statement.Entries, entry => entry.Label == "Late paperwork");
        }
    }

    [Fact]
    public async Task A_draft_cannot_be_printed()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);
        await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);

        await Assert.ThrowsAsync<PortCallStatementNotFinalizedException>(() =>
            PortCallLogScenario.StatementQuery(dbContext).GetPrintAsync(voyageId, stopId, CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_statement_is_reported_as_not_found()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);

        await Assert.ThrowsAsync<PortCallStatementNotFoundException>(() =>
            PortCallLogScenario.StatementQuery(dbContext).GetAsync(voyageId, stopId, CancellationToken.None));
        await Assert.ThrowsAsync<PortCallStatementNotFoundException>(() =>
            new FinalizePortCallStatementService(dbContext, PortCallLogScenario.Clock()).FinalizeAsync(
                voyageId,
                stopId,
                new FinalizePortCallStatementRequest { Version = Guid.NewGuid(), SourceVersion = "ABC" },
                ActorId,
                ActorName,
                CancellationToken.None));
    }

    [Fact]
    public async Task Finalization_requires_the_preview_versions()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);

        var exception = await Assert.ThrowsAsync<PortCallLogValidationException>(() =>
            new FinalizePortCallStatementService(dbContext, PortCallLogScenario.Clock()).FinalizeAsync(
                voyageId,
                stopId,
                new FinalizePortCallStatementRequest(),
                ActorId,
                ActorName,
                CancellationToken.None));

        Assert.Contains(PortCallLogValidator.VersionField, exception.Errors.Keys);
        Assert.Contains(PortCallLogValidator.SourceVersionField, exception.Errors.Keys);
    }

    [Fact]
    public async Task A_delay_recorded_after_the_preview_rejects_finalization_as_a_source_change()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);
        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);

        await PortCallLogScenario.SeedDelayAsync(dbContext, voyageId, stopId, "Tug unavailable", ArrivalUtc.AddHours(1));

        await Assert.ThrowsAsync<PortCallStatementSourceChangedException>(() =>
            PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, preview));

        var statement = await dbContext.Set<PortCallStatement>().SingleAsync();
        Assert.Equal(PortCallStatement.DraftStatus, statement.Status);
        Assert.Empty(await dbContext.Set<PortCallStatementEntry>().ToListAsync());
    }

    [Fact]
    public async Task A_port_time_zone_change_after_the_preview_rejects_finalization_as_a_source_change()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);
        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);

        var port = await dbContext.Set<Port>().SingleAsync();
        dbContext.Entry(port).Property(candidate => candidate.TimeZone).CurrentValue = "Europe/Podgorica";
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        await Assert.ThrowsAsync<PortCallStatementSourceChangedException>(() =>
            PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, preview));

        var statement = await dbContext.Set<PortCallStatement>().SingleAsync();
        Assert.Equal(PortCallStatement.DraftStatus, statement.Status);
        Assert.Null(statement.PortTimeZone);
    }

    [Fact]
    public async Task An_activity_edit_after_the_preview_rejects_finalization_as_a_source_change()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);
        var recorded = await PortCallLogScenario.RecordAsync(
            dbContext,
            voyageId,
            stopId,
            PortCallActivityType.AllFast,
            PortCallLogScenario.At(ArrivalUtc.AddMinutes(10)));
        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);

        await new CorrectPortCallActivityService(dbContext, PortCallLogScenario.Clock()).CorrectAsync(
            voyageId,
            stopId,
            recorded.ActivityId,
            new CorrectPortCallActivityRequest
            {
                ActivityType = PortCallActivityType.AllFast,
                OccurredAt = PortCallLogScenario.At(ArrivalUtc.AddMinutes(12)),
                Reason = "Exact time from the deck log",
                Version = recorded.Version,
            },
            ActorId,
            ActorName,
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        await Assert.ThrowsAsync<PortCallStatementSourceChangedException>(() =>
            PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, preview));
    }

    [Fact]
    public async Task A_stale_statement_version_is_rejected_as_a_conflict()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);
        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);

        await Assert.ThrowsAsync<PortCallLogConflictException>(() =>
            PortCallLogScenario.FinalizeAsync(
                dbContext,
                voyageId,
                stopId,
                preview with { Version = Guid.NewGuid() }));
    }

    [Fact]
    public async Task An_activity_edit_racing_finalization_loses_to_the_committed_finalization()
    {
        var databaseName = $"PortCallLog-{Guid.NewGuid()}";
        await using var setup = PortCallLogScenario.CreateDbContext(databaseName);
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(setup, departed: true);
        await PortCallLogScenario.RecordAsync(
            setup,
            voyageId,
            stopId,
            PortCallActivityType.AllFast,
            PortCallLogScenario.At(ArrivalUtc.AddMinutes(10)));
        var preview = await PortCallLogScenario.CreateStatementAsync(setup, voyageId, stopId);

        await using var editor = PortCallLogScenario.CreateDbContext(databaseName);
        var staleActivity = await editor.Set<PortCallActivity>().SingleAsync();

        await using var finalizer = PortCallLogScenario.CreateDbContext(databaseName);
        await PortCallLogScenario.FinalizeAsync(finalizer, voyageId, stopId, preview);

        await editor.Set<PortCallActivityRevision>().AddAsync(staleActivity.Correct(
            PortCallActivityType.AllFast,
            ArrivalUtc.AddMinutes(11),
            null,
            null,
            "Edited while finalizing",
            ActorId,
            ActorName,
            ArrivalUtc));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => editor.SaveChangesAsync());

        await using var verify = PortCallLogScenario.CreateDbContext(databaseName);
        var stored = await verify.Set<PortCallActivity>().SingleAsync();
        Assert.True(stored.IsLocked);
        Assert.Equal(ArrivalUtc.AddMinutes(10), stored.OccurredAtUtc);
    }

    [Fact]
    public async Task Two_simultaneous_finalizations_let_only_one_commit()
    {
        var databaseName = $"PortCallLog-{Guid.NewGuid()}";
        await using var setup = PortCallLogScenario.CreateDbContext(databaseName);
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(setup, departed: true);
        var preview = await PortCallLogScenario.CreateStatementAsync(setup, voyageId, stopId);

        await using var first = PortCallLogScenario.CreateDbContext(databaseName);
        await using var second = PortCallLogScenario.CreateDbContext(databaseName);
        var secondStatement = await second.Set<PortCallStatement>().SingleAsync();

        await PortCallLogScenario.FinalizeAsync(first, voyageId, stopId, preview);

        secondStatement.Finalize(
            new PortCallStatementSnapshot(
                "VOY-LOG-100",
                1,
                "Port of Bar",
                "MEBAR",
                null,
                "Berth 3",
                ArrivalUtc,
                DepartureUtc,
                preview.SourceVersion!,
                []),
            Guid.NewGuid(),
            "other@harborops.test",
            ArrivalUtc);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var verify = PortCallLogScenario.CreateDbContext(databaseName);
        var stored = await verify.Set<PortCallStatement>().SingleAsync();
        Assert.Equal(ActorId, stored.FinalizedByUserId);
        Assert.Equal(ActorName, stored.FinalizedByName);
    }

    [Fact]
    public async Task Finalizing_does_not_touch_the_stop_or_its_clearance()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);
        var clearance = PortCallClearance.Create(voyageId, stopId);
        await dbContext.Set<PortCallClearance>().AddAsync(clearance);
        await dbContext.SaveChangesAsync();
        var clearanceToken = clearance.ConcurrencyToken;
        dbContext.ChangeTracker.Clear();

        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);
        await PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, preview);

        var stop = await dbContext.Set<VoyageStop>().SingleAsync();
        var storedClearance = await dbContext.Set<PortCallClearance>().SingleAsync();
        Assert.Equal(VoyageStop.DepartedStatus, stop.Status);
        Assert.Equal(ArrivalUtc, stop.Ata);
        Assert.Equal(DepartureUtc, stop.Atd);
        Assert.Equal(PortCallClearance.PendingStatus, storedClearance.Status);
        Assert.Equal(clearanceToken, storedClearance.ConcurrencyToken);
    }
}
