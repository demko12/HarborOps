using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Fleet;

public sealed class GetVesselTypesQueryTests
{
    [Fact]
    public async Task GetAsync_returns_every_type_ordered_by_name()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Set<VesselType>().AddRangeAsync(
            VesselType.Register("Container Ship", "CON", "Container vessel"),
            VesselType.Register("Bulk Carrier", "BULK", "Dry bulk carrier"),
            VesselType.Register("Chemical Tanker", "TANK", "Chemical products tanker"));
        await dbContext.SaveChangesAsync();

        var response = await new GetVesselTypesQuery(dbContext).GetAsync(CancellationToken.None);

        Assert.Equal(
            ["Bulk Carrier", "Chemical Tanker", "Container Ship"],
            response.VesselTypes.Select(type => type.Name));
        Assert.Equal("BULK", response.VesselTypes[0].Code);
    }

    [Fact]
    public async Task GetAsync_returns_an_empty_list_when_no_types_exist()
    {
        await using var dbContext = CreateDbContext();

        var response = await new GetVesselTypesQuery(dbContext).GetAsync(CancellationToken.None);

        Assert.Empty(response.VesselTypes);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
