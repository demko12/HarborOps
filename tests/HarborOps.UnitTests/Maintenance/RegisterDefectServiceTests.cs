using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class RegisterDefectServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;

    [Fact]
    public async Task RegisterAsync_persists_a_reported_defect_when_the_vessel_exists()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var reportedByUserId = Guid.NewGuid();
        var lookup = new FakeVesselExistenceLookup(new VesselExistenceResult(vesselId, "MV Example"));
        var request = CreateValidRequest(vesselId) with { EquipmentId = null };

        var response = await CreateService(dbContext, lookup)
            .RegisterAsync(request, reportedByUserId, CancellationToken.None);

        var defect = await dbContext.Set<VesselDefect>().AsNoTracking().SingleAsync();

        Assert.Equal(defect.Id, response.Id);
        Assert.Equal("Reported", response.Status);
        Assert.Equal(vesselId, defect.VesselId);
        Assert.Null(defect.EquipmentId);
        Assert.Equal("Hydraulic leak on crane boom", defect.Title);
        Assert.Equal("Visible oil leak near the boom cylinder", defect.Description);
        Assert.Equal(DefectSeverity.Medium, defect.ReportedSeverity);
        Assert.Equal(DefectStatus.Reported, defect.Status);
        Assert.Equal(reportedByUserId, defect.ReportedByUserId);
        Assert.Equal(UtcNow, defect.ReportedAtUtc);
        Assert.Equal(1, lookup.CallCount);
        Assert.Equal(vesselId, lookup.LastVesselId);
    }

    [Fact]
    public async Task RegisterAsync_persists_a_defect_linked_to_equipment_on_the_same_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var equipment = await SeedEquipmentAsync(dbContext, vesselId);
        var lookup = new FakeVesselExistenceLookup(new VesselExistenceResult(vesselId, "MV Example"));
        var request = CreateValidRequest(vesselId) with { EquipmentId = equipment.Id };

        await CreateService(dbContext, lookup).RegisterAsync(request, Guid.NewGuid(), CancellationToken.None);

        var defect = await dbContext.Set<VesselDefect>().AsNoTracking().SingleAsync();

        Assert.Equal(equipment.Id, defect.EquipmentId);
    }

    [Fact]
    public async Task RegisterAsync_rejects_a_vessel_the_lookup_does_not_know()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var lookup = new FakeVesselExistenceLookup(result: null);
        var request = CreateValidRequest(vesselId);

        var exception = await Assert.ThrowsAsync<DefectVesselNotFoundException>(
            () => CreateService(dbContext, lookup).RegisterAsync(request, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(vesselId, exception.VesselId);
        Assert.Equal(1, lookup.CallCount);
        Assert.Empty(await dbContext.Set<VesselDefect>().ToListAsync());
    }

    [Fact]
    public async Task RegisterAsync_rejects_equipment_that_does_not_exist()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var missingEquipmentId = Guid.NewGuid();
        var lookup = new FakeVesselExistenceLookup(new VesselExistenceResult(vesselId, "MV Example"));
        var request = CreateValidRequest(vesselId) with { EquipmentId = missingEquipmentId };

        var exception = await Assert.ThrowsAsync<DefectEquipmentNotFoundException>(
            () => CreateService(dbContext, lookup).RegisterAsync(request, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(missingEquipmentId, exception.EquipmentId);
        Assert.Empty(await dbContext.Set<VesselDefect>().ToListAsync());
    }

    [Fact]
    public async Task RegisterAsync_rejects_equipment_belonging_to_a_different_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();
        var foreignEquipment = await SeedEquipmentAsync(dbContext, otherVesselId);
        var lookup = new FakeVesselExistenceLookup(new VesselExistenceResult(vesselId, "MV Example"));
        var request = CreateValidRequest(vesselId) with { EquipmentId = foreignEquipment.Id };

        var exception = await Assert.ThrowsAsync<DefectEquipmentVesselMismatchException>(
            () => CreateService(dbContext, lookup).RegisterAsync(request, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(foreignEquipment.Id, exception.EquipmentId);
        Assert.Equal(vesselId, exception.VesselId);
        Assert.Empty(await dbContext.Set<VesselDefect>().ToListAsync());
    }

    [Fact]
    public async Task RegisterAsync_rejects_invalid_input_before_checking_the_vessel()
    {
        await using var dbContext = CreateDbContext();
        var lookup = new FakeVesselExistenceLookup(new VesselExistenceResult(Guid.NewGuid(), "MV Example"));
        var request = CreateValidRequest(Guid.NewGuid()) with { Title = null };

        await Assert.ThrowsAsync<RegisterDefectValidationException>(
            () => CreateService(dbContext, lookup).RegisterAsync(request, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(0, lookup.CallCount);
        Assert.Empty(await dbContext.Set<VesselDefect>().ToListAsync());
    }

    private static RegisterDefectRequest CreateValidRequest(Guid vesselId)
    {
        return new RegisterDefectRequest
        {
            VesselId = vesselId,
            EquipmentId = null,
            Title = "Hydraulic leak on crane boom",
            Description = "Visible oil leak near the boom cylinder",
            ReportedSeverity = "Medium",
            ObservedAtUtc = new DateTime(2026, 7, 31, 8, 0, 0, DateTimeKind.Utc),
        };
    }

    private static async Task<Equipment> SeedEquipmentAsync(HarborOpsDbContext dbContext, Guid vesselId)
    {
        var equipment = Equipment.Register(vesselId, "Deck crane", "Cargo handling", "Liebherr", null);

        await dbContext.Set<Equipment>().AddAsync(equipment);
        await dbContext.SaveChangesAsync();

        return equipment;
    }

    private static RegisterDefectService CreateService(HarborOpsDbContext dbContext, FakeVesselExistenceLookup lookup)
    {
        return new RegisterDefectService(dbContext, lookup, new FixedTimeProvider(FixedNow));
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
