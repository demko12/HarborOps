using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.UnitTests.Crew;

public sealed class GetVesselManningPlansQueryTests
{
    [Fact]
    public async Task GetAsync_filters_by_vessel_and_paginates_deterministically()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var cook = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Cook");
        var vesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();

        await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28), [(bosun.Id, 1)]);
        await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, vesselId, new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 30), [(bosun.Id, 1), (cook.Id, 2)]);
        await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, otherVesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28), [(bosun.Id, 1)]);

        var query = new GetVesselManningPlansQuery(dbContext);

        var page1 = await query.GetAsync(
            new GetVesselManningPlansRequest { VesselId = vesselId, PageNumber = 1, PageSize = 1 }, CancellationToken.None);
        var page2 = await query.GetAsync(
            new GetVesselManningPlansRequest { VesselId = vesselId, PageNumber = 2, PageSize = 1 }, CancellationToken.None);

        Assert.Equal(2, page1.TotalCount);
        Assert.Single(page1.Items);
        Assert.Single(page2.Items);
        Assert.NotEqual(page1.Items[0].Id, page2.Items[0].Id);
        Assert.All(page1.Items.Concat(page2.Items), item => Assert.Equal(vesselId, item.VesselId));
        Assert.Equal(new DateOnly(2026, 1, 1), page1.Items[0].EffectiveStartDate);
    }

    [Fact]
    public async Task GetAsync_rejects_an_invalid_page_size()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var query = new GetVesselManningPlansQuery(dbContext);

        await Assert.ThrowsAsync<GetVesselManningPlansValidationException>(
            () => query.GetAsync(new GetVesselManningPlansRequest { PageSize = 0 }, CancellationToken.None));
    }
}
