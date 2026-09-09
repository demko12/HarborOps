using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class ApplyDelayImpactServiceTests
{
    [Fact]
    public async Task Apply_stores_the_revised_estimates_without_touching_the_original_plan()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(120);
        var plannedEta = scenario.Stops[0].Eta;
        var plannedEtd = scenario.Stops[0].Etd;

        await scenario.ApplyProposedImpactAsync(delay);

        var stop = await dbContext.Set<VoyageStop>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == scenario.Stops[0].Id);

        Assert.Equal(plannedEta, stop.Eta);
        Assert.Equal(plannedEtd, stop.Etd);
        Assert.Equal(plannedEta.AddMinutes(120), stop.RevisedEta);
        Assert.Equal(plannedEtd.AddMinutes(120), stop.RevisedEtd);
    }

    [Fact]
    public async Task Apply_writes_one_revision_batch_with_the_actor_time_and_old_and_new_values()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60);

        var response = await scenario.ApplyProposedImpactAsync(delay, reason: "Congestion in Bar");

        var batch = Assert.Single(await dbContext.Set<VoyageEstimateRevisionBatch>()
            .AsNoTracking()
            .ToListAsync());

        Assert.Equal(response.RevisionBatchId, batch.Id);
        Assert.Equal(scenario.Voyage.Id, batch.VoyageId);
        Assert.Equal(delay.Id, batch.DelayId);
        Assert.Equal("Congestion in Bar", batch.Reason);
        Assert.Equal(VoyageDelayImpactScenario.ActorId, batch.RevisedByUserId);
        Assert.Equal(VoyageDelayImpactScenario.UtcNow, batch.RevisedAtUtc);

        var items = await dbContext.Set<VoyageEstimateRevisionItem>()
            .AsNoTracking()
            .Where(item => item.RevisionBatchId == batch.Id)
            .ToListAsync();

        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal(60, (item.NewEta - item.PreviousEta).TotalMinutes));
        Assert.All(items, item => Assert.Equal(60, (item.NewEtd - item.PreviousEtd).TotalMinutes));
    }

    [Fact]
    public async Task Apply_returns_the_changes_in_stop_order_with_port_names()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(30);

        var response = await scenario.ApplyProposedImpactAsync(delay);

        Assert.Equal([1, 2, 3], response.Changes.Select(change => change.StopOrder));
        Assert.Equal(
            ["Port of Bar", "Port of Kotor", "Genoa terminal"],
            response.Changes.Select(change => change.PortName));
    }

    [Fact]
    public async Task Apply_revises_a_stop_that_was_already_revised_once()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var firstDelay = await scenario.AddDelayAsync(60);
        await scenario.ApplyProposedImpactAsync(firstDelay);

        var secondDelay = await scenario.AddDelayAsync(30);
        await scenario.ApplyProposedImpactAsync(secondDelay);

        var stop = await dbContext.Set<VoyageStop>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == scenario.Stops[0].Id);

        Assert.Equal(stop.Eta.AddMinutes(90), stop.RevisedEta);
        Assert.Equal(2, await dbContext.Set<VoyageEstimateRevisionBatch>().CountAsync());
    }

    [Fact]
    public async Task Apply_rejects_a_second_revision_for_the_same_delay()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60);
        var preview = await scenario.PreviewAsync(delay);

        await scenario.CreateApplyService().ApplyAsync(
            scenario.Voyage.Id,
            delay.Id,
            VoyageDelayImpactScenario.ActorId,
            VoyageDelayImpactScenario.ToApplyRequest(preview),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DelayImpactAlreadyAppliedException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                VoyageDelayImpactScenario.ToApplyRequest(preview),
                CancellationToken.None));

        Assert.Equal(delay.Id, exception.DelayId);
        Assert.Single(await dbContext.Set<VoyageEstimateRevisionBatch>().ToListAsync());
    }

    [Fact]
    public async Task Apply_rejects_a_stale_preview()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60);
        var preview = await scenario.PreviewAsync(delay);

        var earlierDelay = await scenario.AddDelayAsync(15);
        await scenario.ApplyProposedImpactAsync(earlierDelay);

        var exception = await Assert.ThrowsAsync<StaleDelayImpactPreviewException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                VoyageDelayImpactScenario.ToApplyRequest(preview),
                CancellationToken.None));

        Assert.Equal(scenario.Stops[0].Id, exception.VoyageStopId);
        Assert.Single(await dbContext.Set<VoyageEstimateRevisionBatch>().ToListAsync());
    }

    [Fact]
    public async Task Apply_rejects_an_executed_stop_and_persists_nothing()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60);
        var preview = await scenario.PreviewAsync(delay);

        scenario.Voyage.Depart(VoyageDelayImpactScenario.PlannedDepartureUtc);
        scenario.Stops[0].RecordArrival(new DateTime(2026, 9, 1, 13, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<VoyageStopNotRevisableException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                VoyageDelayImpactScenario.ToApplyRequest(preview),
                CancellationToken.None));

        Assert.Equal(scenario.Stops[0].Id, exception.VoyageStopId);
        Assert.Empty(await dbContext.Set<VoyageEstimateRevisionBatch>().ToListAsync());
    }

    [Fact]
    public async Task Apply_rejects_estimates_that_break_the_stop_chronology()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60, scenario.Stops[0].Id);

        var request = new ApplyDelayImpactRequest
        {
            Reason = VoyageDelayImpactScenario.DefaultReason,
            Stops =
            [
                new ApplyDelayImpactStopRequest
                {
                    VoyageStopId = scenario.Stops[0].Id,
                    ExpectedEta = scenario.Stops[0].Eta,
                    ExpectedEtd = scenario.Stops[0].Etd,
                    Eta = scenario.Stops[0].Eta,
                    Etd = scenario.Stops[1].Eta.AddHours(2),
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<VoyageEstimateChronologyException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                request,
                CancellationToken.None));

        Assert.Equal(scenario.Stops[1].Id, exception.VoyageStopId);
        Assert.Empty(await dbContext.Set<VoyageEstimateRevisionBatch>().ToListAsync());
    }

    [Fact]
    public async Task Apply_rejects_an_estimate_that_precedes_a_recorded_actual_event()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        scenario.Voyage.Depart(VoyageDelayImpactScenario.PlannedDepartureUtc);
        scenario.Stops[0].RecordArrival(new DateTime(2026, 9, 1, 13, 0, 0, DateTimeKind.Utc));
        scenario.Stops[0].RecordDeparture(new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();

        var delay = await scenario.AddDelayAsync(60, scenario.Stops[1].Id);

        var request = new ApplyDelayImpactRequest
        {
            Reason = VoyageDelayImpactScenario.DefaultReason,
            Stops =
            [
                new ApplyDelayImpactStopRequest
                {
                    VoyageStopId = scenario.Stops[1].Id,
                    ExpectedEta = scenario.Stops[1].Eta,
                    ExpectedEtd = scenario.Stops[1].Etd,
                    Eta = scenario.Stops[1].Eta,
                    Etd = scenario.Stops[1].Etd,
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<VoyageEstimateChronologyException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                request,
                CancellationToken.None));

        Assert.Equal(scenario.Stops[1].Id, exception.VoyageStopId);
        Assert.Equal(new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc), exception.EarliestAllowedUtc);
    }

    [Fact]
    public async Task Apply_rejects_a_revised_departure_before_the_revised_arrival()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60, scenario.Stops[2].Id);

        var request = new ApplyDelayImpactRequest
        {
            Reason = VoyageDelayImpactScenario.DefaultReason,
            Stops =
            [
                new ApplyDelayImpactStopRequest
                {
                    VoyageStopId = scenario.Stops[2].Id,
                    ExpectedEta = scenario.Stops[2].Eta,
                    ExpectedEtd = scenario.Stops[2].Etd,
                    Eta = scenario.Stops[2].Etd,
                    Etd = scenario.Stops[2].Eta,
                },
            ],
        };

        await Assert.ThrowsAsync<VoyageEstimateChronologyException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                request,
                CancellationToken.None));

        Assert.Empty(await dbContext.Set<VoyageEstimateRevisionBatch>().ToListAsync());
    }

    [Fact]
    public async Task Apply_leaves_earlier_stops_untouched_when_a_later_stop_fails()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60);

        var request = new ApplyDelayImpactRequest
        {
            Reason = VoyageDelayImpactScenario.DefaultReason,
            Stops =
            [
                new ApplyDelayImpactStopRequest
                {
                    VoyageStopId = scenario.Stops[0].Id,
                    ExpectedEta = scenario.Stops[0].Eta,
                    ExpectedEtd = scenario.Stops[0].Etd,
                    Eta = scenario.Stops[0].Eta.AddHours(1),
                    Etd = scenario.Stops[0].Etd.AddHours(1),
                },
                new ApplyDelayImpactStopRequest
                {
                    VoyageStopId = scenario.Stops[1].Id,
                    ExpectedEta = scenario.Stops[1].Eta.AddHours(5),
                    ExpectedEtd = scenario.Stops[1].Etd,
                    Eta = scenario.Stops[1].Eta.AddHours(1),
                    Etd = scenario.Stops[1].Etd.AddHours(1),
                },
            ],
        };

        await Assert.ThrowsAsync<StaleDelayImpactPreviewException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                request,
                CancellationToken.None));

        var stops = await dbContext.Set<VoyageStop>().AsNoTracking().ToListAsync();

        Assert.All(stops, stop => Assert.Null(stop.RevisedEta));
        Assert.Empty(await dbContext.Set<VoyageEstimateRevisionItem>().ToListAsync());
    }

    [Fact]
    public async Task Apply_rejects_a_missing_reason_and_an_empty_stop_list()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60);

        var exception = await Assert.ThrowsAsync<ApplyDelayImpactValidationException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                new ApplyDelayImpactRequest { Reason = "  ", Stops = [] },
                CancellationToken.None));

        Assert.Contains(ApplyDelayImpactValidator.ReasonField, exception.Errors);
        Assert.Contains(ApplyDelayImpactValidator.StopsField, exception.Errors);
    }

    [Fact]
    public async Task Apply_rejects_the_same_stop_twice_in_one_request()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60);
        var preview = await scenario.PreviewAsync(delay);
        var request = VoyageDelayImpactScenario.ToApplyRequest(preview);

        var duplicated = request with { Stops = [request.Stops![0], request.Stops[0]] };

        var exception = await Assert.ThrowsAsync<ApplyDelayImpactValidationException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                duplicated,
                CancellationToken.None));

        Assert.Contains(ApplyDelayImpactValidator.StopsField, exception.Errors);
    }

    [Fact]
    public async Task Apply_rejects_a_voyage_that_is_no_longer_active()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60);
        var preview = await scenario.PreviewAsync(delay);

        scenario.Voyage.Cancel();
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<VoyageNotRevisableException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                VoyageDelayImpactScenario.ToApplyRequest(preview),
                CancellationToken.None));

        Assert.Equal(Voyage.CancelledStatus, exception.CurrentStatus);
    }

    [Fact]
    public async Task Apply_rejects_a_stop_that_belongs_to_another_voyage()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var other = await VoyageDelayImpactScenario.CreateAsync(dbContext, "VOY-IMPACT-2");
        var delay = await scenario.AddDelayAsync(60);

        var request = new ApplyDelayImpactRequest
        {
            Reason = VoyageDelayImpactScenario.DefaultReason,
            Stops =
            [
                new ApplyDelayImpactStopRequest
                {
                    VoyageStopId = other.Stops[0].Id,
                    ExpectedEta = other.Stops[0].Eta,
                    ExpectedEtd = other.Stops[0].Etd,
                    Eta = other.Stops[0].Eta.AddHours(1),
                    Etd = other.Stops[0].Etd.AddHours(1),
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<VoyageStopNotFoundException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                request,
                CancellationToken.None));

        Assert.Equal(other.Stops[0].Id, exception.VoyageStopId);
    }

    [Fact]
    public async Task Apply_rejects_a_delay_from_another_voyage()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var other = await VoyageDelayImpactScenario.CreateAsync(dbContext, "VOY-IMPACT-2");
        var foreignDelay = await other.AddDelayAsync(60);
        var preview = await other.PreviewAsync(foreignDelay);

        var exception = await Assert.ThrowsAsync<DelayNotFoundException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                foreignDelay.Id,
                VoyageDelayImpactScenario.ActorId,
                VoyageDelayImpactScenario.ToApplyRequest(preview),
                CancellationToken.None));

        Assert.Equal(foreignDelay.Id, exception.DelayId);
    }

    [Fact]
    public async Task Apply_revises_the_departure_of_the_stop_in_progress_and_keeps_its_arrival()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var actualArrival = new DateTime(2026, 9, 1, 13, 0, 0, DateTimeKind.Utc);
        scenario.Voyage.Depart(VoyageDelayImpactScenario.PlannedDepartureUtc);
        scenario.Stops[0].RecordArrival(actualArrival);
        await dbContext.SaveChangesAsync();

        var delay = await scenario.AddDelayAsync(120);
        var plannedEta = scenario.Stops[0].Eta;
        var plannedEtd = scenario.Stops[0].Etd;

        await scenario.ApplyProposedImpactAsync(delay);

        Assert.Equal(plannedEta, scenario.Stops[0].Eta);
        Assert.Equal(plannedEtd, scenario.Stops[0].Etd);
        Assert.Equal(plannedEta, scenario.Stops[0].RevisedEta);
        Assert.Equal(plannedEtd.AddMinutes(120), scenario.Stops[0].RevisedEtd);
        Assert.Equal(actualArrival, scenario.Stops[0].Ata);
        Assert.Null(scenario.Stops[0].Atd);
    }

    [Fact]
    public async Task Apply_rejects_a_moved_arrival_for_the_stop_in_progress()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        scenario.Voyage.Depart(VoyageDelayImpactScenario.PlannedDepartureUtc);
        scenario.Stops[0].RecordArrival(new DateTime(2026, 9, 1, 13, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();

        var delay = await scenario.AddDelayAsync(60);

        var request = new ApplyDelayImpactRequest
        {
            Reason = VoyageDelayImpactScenario.DefaultReason,
            Stops =
            [
                new ApplyDelayImpactStopRequest
                {
                    VoyageStopId = scenario.Stops[0].Id,
                    ExpectedEta = scenario.Stops[0].Eta,
                    ExpectedEtd = scenario.Stops[0].Etd,
                    Eta = scenario.Stops[0].Eta.AddMinutes(60),
                    Etd = scenario.Stops[0].Etd.AddMinutes(60),
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<VoyageStopNotRevisableException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                request,
                CancellationToken.None));

        Assert.Equal(scenario.Stops[0].Id, exception.VoyageStopId);
        Assert.Empty(await dbContext.Set<VoyageEstimateRevisionBatch>().ToListAsync());
    }

    [Fact]
    public async Task Apply_rejects_a_departed_stop_even_with_unchanged_estimates()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        scenario.Voyage.Depart(VoyageDelayImpactScenario.PlannedDepartureUtc);
        scenario.Stops[0].RecordArrival(new DateTime(2026, 9, 1, 13, 0, 0, DateTimeKind.Utc));
        scenario.Stops[0].RecordDeparture(new DateTime(2026, 9, 1, 19, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();

        var delay = await scenario.AddDelayAsync(60);

        var request = new ApplyDelayImpactRequest
        {
            Reason = VoyageDelayImpactScenario.DefaultReason,
            Stops =
            [
                new ApplyDelayImpactStopRequest
                {
                    VoyageStopId = scenario.Stops[0].Id,
                    ExpectedEta = scenario.Stops[0].Eta,
                    ExpectedEtd = scenario.Stops[0].Etd,
                    Eta = scenario.Stops[0].Eta,
                    Etd = scenario.Stops[0].Etd,
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<VoyageStopNotRevisableException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                request,
                CancellationToken.None));

        Assert.Equal(scenario.Stops[0].Id, exception.VoyageStopId);
        Assert.Empty(await dbContext.Set<VoyageEstimateRevisionBatch>().ToListAsync());
    }

    [Fact]
    public async Task Apply_propagates_the_revised_departure_of_the_stop_in_progress_downstream()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        scenario.Voyage.Depart(VoyageDelayImpactScenario.PlannedDepartureUtc);
        scenario.Stops[0].RecordArrival(new DateTime(2026, 9, 1, 13, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();

        var delay = await scenario.AddDelayAsync(60);
        var revisedEtd = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);

        var request = new ApplyDelayImpactRequest
        {
            Reason = VoyageDelayImpactScenario.DefaultReason,
            Stops =
            [
                new ApplyDelayImpactStopRequest
                {
                    VoyageStopId = scenario.Stops[0].Id,
                    ExpectedEta = scenario.Stops[0].Eta,
                    ExpectedEtd = scenario.Stops[0].Etd,
                    Eta = scenario.Stops[0].Eta,
                    Etd = revisedEtd,
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<VoyageEstimateChronologyException>(
            () => scenario.CreateApplyService().ApplyAsync(
                scenario.Voyage.Id,
                delay.Id,
                VoyageDelayImpactScenario.ActorId,
                request,
                CancellationToken.None));

        Assert.Equal(scenario.Stops[1].Id, exception.VoyageStopId);
        Assert.Equal(revisedEtd, exception.EarliestAllowedUtc);
        Assert.Empty(await dbContext.Set<VoyageEstimateRevisionBatch>().ToListAsync());
    }
}
