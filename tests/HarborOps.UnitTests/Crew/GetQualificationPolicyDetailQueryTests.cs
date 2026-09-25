using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class GetQualificationPolicyDetailQueryTests
{
    [Fact]
    public async Task GetAsync_returns_requirements_and_history_newest_first()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var policy = await QualificationPolicyTestData.SeedPolicyAsync(dbContext, bosun.Id, true, ["STCW-II/1"]);

        var firstUpdateService = new UpdateQualificationPolicyService(
            dbContext, new FixedTimeProvider(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)));
        await firstUpdateService.UpdateAsync(
            policy.Id, Guid.NewGuid(),
            new UpdateQualificationPolicyRequest
            {
                Enabled = true,
                Requirements = [new QualificationRequirementInput { RequiredType = "STCW-VI/1" }],
            },
            CancellationToken.None);

        var secondUpdateService = new UpdateQualificationPolicyService(
            dbContext, new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));
        await secondUpdateService.UpdateAsync(
            policy.Id, Guid.NewGuid(),
            new UpdateQualificationPolicyRequest
            {
                Enabled = false,
                Requirements = [new QualificationRequirementInput { RequiredType = "STCW-VI/2" }],
            },
            CancellationToken.None);

        var query = new GetQualificationPolicyDetailQuery(dbContext);

        var response = await query.GetAsync(policy.Id, CancellationToken.None);

        Assert.Equal(policy.Id, response.Id);
        Assert.Equal("Bosun", response.RankName);
        Assert.Equal(["STCW-VI/2"], response.RequiredTypes);
        Assert.Equal(2, response.History.Count);
        Assert.Equal(CrewQualificationPolicyHistory.UpdatedEvent, response.History[0].EventType);
        Assert.Equal(CrewQualificationPolicyHistory.UpdatedEvent, response.History[1].EventType);
        Assert.Equal(["STCW-VI/1"], response.History[0].PreviousRequiredTypes);
        Assert.Equal(["STCW-VI/2"], response.History[0].NewRequiredTypes);
        Assert.Equal(["STCW-II/1"], response.History[1].PreviousRequiredTypes);
        Assert.Equal(["STCW-VI/1"], response.History[1].NewRequiredTypes);
    }

    [Fact]
    public async Task GetAsync_rejects_a_missing_policy()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var query = new GetQualificationPolicyDetailQuery(dbContext);

        await Assert.ThrowsAsync<CrewQualificationPolicyNotFoundException>(
            () => query.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
