using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.UnitTests.Voyages;

public sealed class GetVoyageEstimateRevisionsQueryTests
{
    [Fact]
    public async Task Query_returns_the_planned_and_the_current_revised_estimates()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(120, scenario.Stops[1].Id);
        await scenario.ApplyProposedImpactAsync(delay);

        var response = await CreateQuery(scenario).GetEstimateRevisionsAsync(
            scenario.Voyage.Id,
            CancellationToken.None);

        Assert.Equal([1, 2, 3], response.Stops.Select(stop => stop.StopOrder));

        var untouched = response.Stops[0];
        Assert.Equal(scenario.Stops[0].Eta, untouched.PlannedEta);
        Assert.Null(untouched.RevisedEta);
        Assert.Null(untouched.RevisedEtd);

        var revised = response.Stops[1];
        Assert.Equal(scenario.Stops[1].Eta, revised.PlannedEta);
        Assert.Equal(scenario.Stops[1].Etd, revised.PlannedEtd);
        Assert.Equal(scenario.Stops[1].Eta.AddMinutes(120), revised.RevisedEta);
        Assert.Equal(scenario.Stops[1].Etd.AddMinutes(120), revised.RevisedEtd);
        Assert.Equal("Port of Kotor", revised.PortName);
    }

    [Fact]
    public async Task Query_returns_the_revision_history_newest_first()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);

        var firstDelay = await scenario.AddDelayAsync(30, scenario.Stops[2].Id);
        var first = await scenario.ApplyProposedImpactAsync(firstDelay, reason: "First revision");

        scenario.Advance(TimeSpan.FromMinutes(5));

        var secondDelay = await scenario.AddDelayAsync(45, scenario.Stops[1].Id);
        var second = await scenario.ApplyProposedImpactAsync(secondDelay, reason: "Second revision");

        var response = await CreateQuery(scenario).GetEstimateRevisionsAsync(
            scenario.Voyage.Id,
            CancellationToken.None);

        Assert.Equal(2, response.Revisions.Count);
        Assert.Equal(
            [second.RevisionBatchId, first.RevisionBatchId],
            response.Revisions.Select(revision => revision.RevisionBatchId));
        Assert.Equal("Second revision", response.Revisions[0].Reason);
        Assert.Equal(secondDelay.Id, response.Revisions[0].DelayId);
        Assert.Equal(VoyageDelayImpactScenario.ActorId, response.Revisions[0].RevisedByUserId);
        Assert.Equal(
            VoyageDelayImpactScenario.UtcNow.AddMinutes(5),
            response.Revisions[0].RevisedAtUtc);
        Assert.Equal(VoyageDelayImpactScenario.UtcNow, response.Revisions[1].RevisedAtUtc);
    }

    [Fact]
    public async Task Query_orders_the_changes_of_a_revision_by_stop_order()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var delay = await scenario.AddDelayAsync(60);
        await scenario.ApplyProposedImpactAsync(delay);

        var response = await CreateQuery(scenario).GetEstimateRevisionsAsync(
            scenario.Voyage.Id,
            CancellationToken.None);

        var revision = Assert.Single(response.Revisions);

        Assert.Equal([1, 2, 3], revision.Changes.Select(change => change.StopOrder));
        Assert.Equal(
            ["Port of Bar", "Port of Kotor", "Genoa terminal"],
            revision.Changes.Select(change => change.PortName));
        Assert.All(
            revision.Changes,
            change => Assert.Equal(60, (change.NewEta - change.PreviousEta).TotalMinutes));
    }

    [Fact]
    public async Task Query_returns_an_empty_history_before_any_revision()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);

        var response = await CreateQuery(scenario).GetEstimateRevisionsAsync(
            scenario.Voyage.Id,
            CancellationToken.None);

        Assert.Empty(response.Revisions);
        Assert.Equal(3, response.Stops.Count);
    }

    [Fact]
    public async Task Query_ignores_revisions_that_belong_to_another_voyage()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var other = await VoyageDelayImpactScenario.CreateAsync(dbContext, "VOY-IMPACT-2");
        var foreignDelay = await other.AddDelayAsync(60);
        await other.ApplyProposedImpactAsync(foreignDelay);

        var response = await CreateQuery(scenario).GetEstimateRevisionsAsync(
            scenario.Voyage.Id,
            CancellationToken.None);

        Assert.Empty(response.Revisions);
    }

    [Fact]
    public async Task Query_rejects_an_unknown_voyage()
    {
        await using var dbContext = VoyageDelayImpactScenario.CreateDbContext();
        var scenario = await VoyageDelayImpactScenario.CreateAsync(dbContext);
        var unknownVoyageId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VoyageNotFoundException>(
            () => CreateQuery(scenario).GetEstimateRevisionsAsync(
                unknownVoyageId,
                CancellationToken.None));

        Assert.Equal(unknownVoyageId, exception.VoyageId);
    }

    private static IGetVoyageEstimateRevisionsQuery CreateQuery(VoyageDelayImpactScenario scenario)
    {
        return new GetVoyageEstimateRevisionsQuery(scenario.DbContext);
    }
}
