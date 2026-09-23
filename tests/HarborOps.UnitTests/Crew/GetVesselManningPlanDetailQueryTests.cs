using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.UnitTests.Crew;

public sealed class GetVesselManningPlanDetailQueryTests
{
    [Fact]
    public async Task GetAsync_returns_lines_with_rank_names_and_history_newest_first()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var plan = await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), [(bosun.Id, 2)]);

        var replaceService = new ReplaceVesselManningPlanLinesService(dbContext, new FixedTimeProvider(new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero)));
        await replaceService.ReplaceAsync(
            plan.Id,
            Guid.NewGuid(),
            new ReplaceVesselManningPlanLinesRequest
            {
                Reason = "First revision.",
                Lines = [new ManningPlanLineInput { RankId = bosun.Id, RequiredHeadcount = 3 }],
            },
            CancellationToken.None);

        var laterReplaceService = new ReplaceVesselManningPlanLinesService(dbContext, new FixedTimeProvider(new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero)));
        await laterReplaceService.ReplaceAsync(
            plan.Id,
            Guid.NewGuid(),
            new ReplaceVesselManningPlanLinesRequest
            {
                Reason = "Second revision.",
                Lines = [new ManningPlanLineInput { RankId = bosun.Id, RequiredHeadcount = 4 }],
            },
            CancellationToken.None);

        var query = new GetVesselManningPlanDetailQuery(dbContext);

        var response = await query.GetAsync(plan.Id, CancellationToken.None);

        Assert.Equal(plan.Id, response.Id);
        Assert.Single(response.Lines);
        Assert.Equal("Bosun", response.Lines[0].RankName);
        Assert.Equal(4, response.Lines[0].RequiredHeadcount);
        Assert.Equal(2, response.History.Count);
        Assert.Equal("Second revision.", response.History[0].Reason);
        Assert.Equal("First revision.", response.History[1].Reason);
    }

    [Fact]
    public async Task GetAsync_rejects_a_missing_plan()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var query = new GetVesselManningPlanDetailQuery(dbContext);

        await Assert.ThrowsAsync<VesselManningPlanNotFoundException>(
            () => query.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
