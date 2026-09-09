using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class DelayImpactPreviewServiceTests
{
    [Fact]
    public async Task Preview_shifts_every_planned_stop_for_a_voyage_level_delay()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(120);

        var preview = await new DelayImpactPreviewService(dbContext).PreviewAsync(
            scenario.Voyage.Id,
            delay.Id,
            CancellationToken.None);

        Assert.Equal(120, preview.DelayMinutes);
        Assert.Equal(3, preview.Stops.Count);
        Assert.Equal([1, 2, 3], preview.Stops.Select(stop => stop.StopOrder));

        var first = preview.Stops[0];
        Assert.Equal(scenario.Stops[0].Eta, first.CurrentEta);
        Assert.Equal(scenario.Stops[0].Eta.AddMinutes(120), first.ProposedEta);
        Assert.Equal(scenario.Stops[0].Etd.AddMinutes(120), first.ProposedEtd);
        Assert.Equal(120, first.EtaDeltaMinutes);
        Assert.Equal(120, first.EtdDeltaMinutes);
        Assert.Equal("Port of Bar", first.PortName);
    }

    [Fact]
    public async Task Preview_reports_the_planned_arrival_impact()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(90);

        var preview = await new DelayImpactPreviewService(dbContext).PreviewAsync(
            scenario.Voyage.Id,
            delay.Id,
            CancellationToken.None);

        Assert.Equal(VoyageDelayImpactScenario.PlannedArrivalUtc, preview.PlannedArrival);
        Assert.Equal(
            VoyageDelayImpactScenario.PlannedArrivalUtc.AddMinutes(90),
            preview.ProposedArrival);
        Assert.Equal(90, preview.PlannedArrivalDeltaMinutes);
    }

    [Fact]
    public async Task Preview_only_shifts_the_delayed_stop_and_the_stops_after_it()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60, scenario.Stops[1].Id);

        var preview = await new DelayImpactPreviewService(dbContext).PreviewAsync(
            scenario.Voyage.Id,
            delay.Id,
            CancellationToken.None);

        Assert.Equal([2, 3], preview.Stops.Select(stop => stop.StopOrder));
    }

    [Fact]
    public async Task Preview_leaves_executed_stops_out_of_the_proposal()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        scenario.Voyage.Depart(VoyageDelayImpactScenario.PlannedDepartureUtc);
        scenario.Stops[0].RecordArrival(new DateTime(2026, 9, 1, 13, 0, 0, DateTimeKind.Utc));
        scenario.Stops[0].RecordDeparture(new DateTime(2026, 9, 1, 19, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();

        var delay = await scenario.AddDelayAsync(45);

        var preview = await new DelayImpactPreviewService(dbContext).PreviewAsync(
            scenario.Voyage.Id,
            delay.Id,
            CancellationToken.None);

        Assert.Equal([2, 3], preview.Stops.Select(stop => stop.StopOrder));
        Assert.DoesNotContain(preview.Stops, stop => stop.StopId == scenario.Stops[0].Id);
    }

    [Fact]
    public async Task Preview_reports_a_berth_conflict_for_the_proposed_window()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var berth = await scenario.ReserveBerthAsync(
            scenario.Stops[0],
            Guid.NewGuid(),
            new DateTime(2026, 9, 1, 19, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 23, 0, 0, DateTimeKind.Utc));

        var delay = await scenario.AddDelayAsync(120, scenario.Stops[0].Id);

        var preview = await new DelayImpactPreviewService(dbContext).PreviewAsync(
            scenario.Voyage.Id,
            delay.Id,
            CancellationToken.None);

        var warning = Assert.Single(preview.Warnings);
        Assert.Equal(scenario.Stops[0].Id, warning.StopId);
        Assert.Equal(1, warning.StopOrder);
        Assert.Equal(berth.Id, warning.BerthId);
        Assert.Equal("Berth 1", warning.BerthName);
    }

    [Fact]
    public async Task Preview_ignores_a_berth_reservation_that_does_not_overlap()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        await scenario.ReserveBerthAsync(
            scenario.Stops[0],
            Guid.NewGuid(),
            new DateTime(2026, 9, 2, 6, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc));

        var delay = await scenario.AddDelayAsync(60, scenario.Stops[0].Id);

        var preview = await new DelayImpactPreviewService(dbContext).PreviewAsync(
            scenario.Voyage.Id,
            delay.Id,
            CancellationToken.None);

        Assert.Empty(preview.Warnings);
    }

    [Fact]
    public async Task Preview_persists_nothing()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(120);

        await new DelayImpactPreviewService(dbContext).PreviewAsync(
            scenario.Voyage.Id,
            delay.Id,
            CancellationToken.None);

        Assert.Empty(await dbContext.Set<VoyageEstimateRevisionBatch>().ToListAsync());
        Assert.All(
            await dbContext.Set<VoyageStop>().AsNoTracking().ToListAsync(),
            stop =>
            {
                Assert.Null(stop.RevisedEta);
                Assert.Null(stop.RevisedEtd);
            });
    }

    [Fact]
    public async Task Preview_rejects_an_unknown_voyage()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var unknownVoyageId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VoyageNotFoundException>(
            () => new DelayImpactPreviewService(dbContext).PreviewAsync(
                unknownVoyageId,
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal(unknownVoyageId, exception.VoyageId);
    }

    [Fact]
    public async Task Preview_rejects_a_delay_from_another_voyage()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var other = await VoyageDelayImpactScenario.CreateAsync(dbContext, "VOY-IMPACT-2");
        var foreignDelay = await other.AddDelayAsync(30);

        var exception = await Assert.ThrowsAsync<DelayNotFoundException>(
            () => new DelayImpactPreviewService(dbContext).PreviewAsync(
                scenario.Voyage.Id,
                foreignDelay.Id,
                CancellationToken.None));

        Assert.Equal(foreignDelay.Id, exception.DelayId);
    }

    [Fact]
    public async Task Preview_rejects_a_voyage_that_is_no_longer_active()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(30);
        scenario.Voyage.Cancel();
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<VoyageNotRevisableException>(
            () => new DelayImpactPreviewService(dbContext).PreviewAsync(
                scenario.Voyage.Id,
                delay.Id,
                CancellationToken.None));

        Assert.Equal(Voyage.CancelledStatus, exception.CurrentStatus);
    }

    [Fact]
    public async Task Preview_rejects_a_delay_whose_impact_was_already_applied()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(30);

        await scenario.ApplyProposedImpactAsync(delay);

        var exception = await Assert.ThrowsAsync<DelayImpactAlreadyAppliedException>(
            () => new DelayImpactPreviewService(dbContext).PreviewAsync(
                scenario.Voyage.Id,
                delay.Id,
                CancellationToken.None));

        Assert.Equal(delay.Id, exception.DelayId);
    }

    [Fact]
    public async Task Preview_moves_only_the_departure_of_the_stop_in_progress()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        scenario.Voyage.Depart(VoyageDelayImpactScenario.PlannedDepartureUtc);
        scenario.Stops[0].RecordArrival(new DateTime(2026, 9, 1, 13, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();

        var delay = await scenario.AddDelayAsync(120);

        var preview = await new DelayImpactPreviewService(dbContext).PreviewAsync(
            scenario.Voyage.Id,
            delay.Id,
            CancellationToken.None);

        var current = preview.Stops.Single(stop => stop.StopId == scenario.Stops[0].Id);

        Assert.Equal([1, 2, 3], preview.Stops.Select(stop => stop.StopOrder));
        Assert.Equal(0, current.EtaDeltaMinutes);
        Assert.Equal(scenario.Stops[0].Eta, current.ProposedEta);
        Assert.Equal(120, current.EtdDeltaMinutes);
        Assert.Equal(new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc), current.ProposedEtd);
    }
}
