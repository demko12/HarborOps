using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.UnitTests.Crew;

public sealed class GetQualificationPoliciesQueryTests
{
    [Fact]
    public async Task GetAsync_filters_by_crew_position_and_paginates_deterministically()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var cook = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Cook");

        await QualificationPolicyTestData.SeedPolicyAsync(dbContext, bosun.Id, true, ["STCW-II/1"]);
        await QualificationPolicyTestData.SeedPolicyAsync(dbContext, cook.Id, false, ["STCW-VI/1", "STCW-VI/2"]);

        var query = new GetQualificationPoliciesQuery(dbContext);

        var filtered = await query.GetAsync(
            new GetQualificationPoliciesRequest { CrewPositionId = cook.Id }, CancellationToken.None);

        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal("Cook", filtered.Items[0].RankName);
        Assert.False(filtered.Items[0].Enabled);
        Assert.Equal(2, filtered.Items[0].RequiredTypeCount);
    }

    [Fact]
    public async Task GetAsync_rejects_an_invalid_page_size()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var query = new GetQualificationPoliciesQuery(dbContext);

        await Assert.ThrowsAsync<GetQualificationPoliciesValidationException>(
            () => query.GetAsync(new GetQualificationPoliciesRequest { PageSize = 0 }, CancellationToken.None));
    }
}
