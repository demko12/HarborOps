using HarborOps.Api.Composition.Maintenance;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Operations;

// Proves the real Maintenance -> IVesselExistenceLookup -> FleetVesselExistenceLookup ->
// Fleet's own VesselLookupService -> DbSet<Vessel> path behaves identically to the faked
// path already covered by RegisterDefectServiceTests, without Maintenance ever touching
// DbSet<Vessel> directly - it only ever sees Fleet through the port/adapter seam.
// This is an in-process composition test: no WebApplicationFactory, no HTTP calls, no
// running API host - it wires plain CLR objects (a real DbContext, a real Fleet Application
// service, the real Api-layer adapter, and a real Maintenance Application service) directly.
public sealed class RegisterDefectThroughFleetVesselLookupTests
{
    [Fact]
    public async Task RegisterAsync_succeeds_when_the_real_fleet_lookup_finds_the_seeded_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vessel = Vessel.Register(Guid.NewGuid(), Guid.NewGuid(), "MV Example", "1234567", "Malta", "Active");
        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        var service = CreateRegisterDefectService(dbContext);
        var reportedByUserId = Guid.NewGuid();
        var request = new RegisterDefectRequest
        {
            VesselId = vessel.Id,
            EquipmentId = null,
            Title = "Hydraulic leak on crane boom",
            Description = "Visible oil leak near the boom cylinder",
            ReportedSeverity = "Medium",
            ObservedAtUtc = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
        };

        var response = await service.RegisterAsync(request, reportedByUserId, CancellationToken.None);

        var defect = await dbContext.Set<VesselDefect>().AsNoTracking().SingleAsync();

        Assert.Equal(defect.Id, response.Id);
        Assert.Equal("Reported", response.Status);
        Assert.Equal(vessel.Id, defect.VesselId);
        Assert.Equal(reportedByUserId, defect.ReportedByUserId);
        Assert.Equal(DefectStatus.Reported, defect.Status);
    }

    [Fact]
    public async Task RegisterAsync_throws_not_found_when_the_real_fleet_lookup_has_no_matching_vessel()
    {
        await using var dbContext = CreateDbContext();
        var seededVessel = Vessel.Register(Guid.NewGuid(), Guid.NewGuid(), "MV Example", "1234567", "Malta", "Active");
        await dbContext.Set<Vessel>().AddAsync(seededVessel);
        await dbContext.SaveChangesAsync();

        var service = CreateRegisterDefectService(dbContext);
        var unknownVesselId = Guid.NewGuid();
        var request = new RegisterDefectRequest
        {
            VesselId = unknownVesselId,
            EquipmentId = null,
            Title = "Hydraulic leak on crane boom",
            Description = "Visible oil leak near the boom cylinder",
            ReportedSeverity = "Medium",
            ObservedAtUtc = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
        };

        var exception = await Assert.ThrowsAsync<DefectVesselNotFoundException>(
            () => service.RegisterAsync(request, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(unknownVesselId, exception.VesselId);
        Assert.Empty(await dbContext.Set<VesselDefect>().ToListAsync());
    }

    private static RegisterDefectService CreateRegisterDefectService(HarborOpsDbContext dbContext)
    {
        var fleetLookupService = new VesselLookupService(dbContext);
        var adapter = new FleetVesselExistenceLookup(fleetLookupService);

        return new RegisterDefectService(dbContext, adapter, TimeProvider.System);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
