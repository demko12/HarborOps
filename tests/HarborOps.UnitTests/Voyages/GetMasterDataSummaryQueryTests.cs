using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class GetMasterDataSummaryQueryTests
{
    [Fact]
    public async Task Get_counts_every_port_berth_and_route_and_averages_all_route_distances()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var kotorId = await SeedPortAsync(dbContext, "Port of Kotor");
        await SeedPortAsync(dbContext, "Port of Budva");
        await dbContext.Set<Berth>().AddRangeAsync(
            [
                Berth.Create(barId, "Berth 1", 210m, 12m, "Available"),
                Berth.Create(barId, "Berth 2", 180m, 10m, "Out of service"),
                Berth.Create(kotorId, "Berth 1", 150m, 8m, "Available"),
            ],
            CancellationToken.None);
        await dbContext.Set<Route>().AddRangeAsync(
            [
                Route.Create("Bar - Kotor", 40m, 4),
                Route.Create("Bar - Bari", 110m, 10),
            ],
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetMasterDataSummaryQuery(dbContext).GetAsync(CancellationToken.None);

        Assert.Equal(3, response.PortCount);
        Assert.Equal(3, response.BerthCount);
        Assert.Equal(2, response.RouteCount);
        Assert.Equal(75m, response.AverageRouteDistance);
    }

    [Fact]
    public async Task Get_returns_zeros_when_the_directory_is_empty()
    {
        await using var dbContext = CreateDbContext();

        var response = await new GetMasterDataSummaryQuery(dbContext).GetAsync(CancellationToken.None);

        Assert.Equal(0, response.PortCount);
        Assert.Equal(0, response.BerthCount);
        Assert.Equal(0, response.RouteCount);
        Assert.Equal(0m, response.AverageRouteDistance);
    }

    private static async Task<Guid> SeedPortAsync(HarborOpsDbContext dbContext, string name)
    {
        var port = Port.Create(name, "Montenegro");

        await dbContext.Set<Port>().AddAsync(port, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return port.Id;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
