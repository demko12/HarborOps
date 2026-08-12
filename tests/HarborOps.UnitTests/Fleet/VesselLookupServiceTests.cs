using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Fleet;

public sealed class VesselLookupServiceTests
{
    [Fact]
    public async Task LookupAsync_returns_names_for_requested_vessels()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var first = await SeedVesselAsync(dbContext, vesselTypeId, "Aurora", "1000001");
        var second = await SeedVesselAsync(dbContext, vesselTypeId, "Borealis", "1000002");
        await SeedVesselAsync(dbContext, vesselTypeId, "Not requested", "1000003");

        var query = new VesselLookupService(dbContext);

        var response = await query.LookupAsync(
            new VesselLookupRequest { VesselIds = [first.Id, second.Id] },
            CancellationToken.None);

        Assert.Equal(2, response.Vessels.Count);
        Assert.Contains(response.Vessels, vessel => vessel.Id == first.Id && vessel.Name == "Aurora");
        Assert.Contains(response.Vessels, vessel => vessel.Id == second.Id && vessel.Name == "Borealis");
    }

    [Fact]
    public async Task LookupAsync_ignores_unknown_vessel_ids()
    {
        await using var dbContext = CreateDbContext();
        var query = new VesselLookupService(dbContext);

        var response = await query.LookupAsync(
            new VesselLookupRequest { VesselIds = [Guid.NewGuid()] },
            CancellationToken.None);

        Assert.Empty(response.Vessels);
    }

    [Fact]
    public async Task LookupAsync_deduplicates_requested_ids()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId, "Aurora", "1000001");

        var query = new VesselLookupService(dbContext);

        var response = await query.LookupAsync(
            new VesselLookupRequest { VesselIds = [vessel.Id, vessel.Id] },
            CancellationToken.None);

        Assert.Single(response.Vessels);
    }

    [Fact]
    public async Task LookupAsync_rejects_missing_vessel_ids()
    {
        await using var dbContext = CreateDbContext();
        var query = new VesselLookupService(dbContext);

        var exception = await Assert.ThrowsAsync<VesselLookupValidationException>(
            () => query.LookupAsync(new VesselLookupRequest(), CancellationToken.None));

        Assert.Contains(VesselLookupValidator.VesselIdsField, exception.Errors);
    }

    [Fact]
    public async Task LookupAsync_rejects_empty_vessel_id_in_list()
    {
        await using var dbContext = CreateDbContext();
        var query = new VesselLookupService(dbContext);

        var exception = await Assert.ThrowsAsync<VesselLookupValidationException>(
            () => query.LookupAsync(
                new VesselLookupRequest { VesselIds = [Guid.NewGuid(), Guid.Empty] },
                CancellationToken.None));

        Assert.Contains(VesselLookupValidator.VesselIdsField, exception.Errors);
    }

    [Fact]
    public async Task LookupAsync_rejects_more_than_100_distinct_ids()
    {
        await using var dbContext = CreateDbContext();
        var query = new VesselLookupService(dbContext);
        var vesselIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();

        var exception = await Assert.ThrowsAsync<VesselLookupValidationException>(
            () => query.LookupAsync(
                new VesselLookupRequest { VesselIds = vesselIds },
                CancellationToken.None));

        Assert.Contains(VesselLookupValidator.VesselIdsField, exception.Errors);
    }

    private static async Task<Guid> SeedVesselTypeAsync(HarborOpsDbContext dbContext)
    {
        var vesselType = VesselType.Register("Container Ship", "CNTR", "Container carrying vessel");

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.SaveChangesAsync();

        return vesselType.Id;
    }

    private static async Task<Vessel> SeedVesselAsync(
        HarborOpsDbContext dbContext,
        Guid vesselTypeId,
        string name,
        string imoNumber)
    {
        var vessel = Vessel.Register(Guid.NewGuid(), vesselTypeId, name, imoNumber, "Malta", "Active");

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
