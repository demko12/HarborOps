using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Fleet;

public sealed class UpdateVesselSpecificationsServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateAsync_rejects_non_positive_measurements()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<UpdateVesselSpecificationsValidationException>(
            () => service.UpdateAsync(
                Guid.NewGuid(),
                new UpdateVesselSpecificationsRequest { Dwt = 0m, Gt = -1m, Loa = 190m },
                CancellationToken.None));

        Assert.Contains(UpdateVesselSpecificationsValidator.DwtField, exception.Errors);
        Assert.Contains(UpdateVesselSpecificationsValidator.GtField, exception.Errors);
        Assert.DoesNotContain(UpdateVesselSpecificationsValidator.LoaField, exception.Errors);
    }

    [Fact]
    public async Task UpdateAsync_throws_not_found_for_unknown_vessel()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var vesselId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VesselNotFoundException>(
            () => service.UpdateAsync(
                vesselId,
                new UpdateVesselSpecificationsRequest { Dwt = 52000m },
                CancellationToken.None));

        Assert.Equal(vesselId, exception.VesselId);
    }

    [Fact]
    public async Task UpdateAsync_stores_measurements_and_stamps_the_time_provider_instant()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId);
        var service = CreateService(dbContext);

        var response = await service.UpdateAsync(
            vessel.Id,
            new UpdateVesselSpecificationsRequest
            {
                Dwt = 52000m,
                Gt = 29000m,
                Loa = 190m,
                Beam = 32m,
                Draft = 12m,
            },
            CancellationToken.None);

        var persisted = await dbContext.Set<Vessel>().AsNoTracking().SingleAsync(v => v.Id == vessel.Id);

        Assert.Equal(52000m, persisted.Dwt);
        Assert.Equal(29000m, persisted.Gt);
        Assert.Equal(190m, persisted.Loa);
        Assert.Equal(32m, persisted.Beam);
        Assert.Equal(12m, persisted.Draft);
        Assert.Equal(FixedNow.UtcDateTime, persisted.UpdatedAtUtc);
        Assert.Equal(52000m, response.Dwt);
    }

    [Fact]
    public async Task UpdateAsync_can_clear_a_measurement()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId);
        await SetSpecificationsAsync(dbContext, vessel.Id, 40000m);
        var service = CreateService(dbContext);

        await service.UpdateAsync(
            vessel.Id,
            new UpdateVesselSpecificationsRequest { Dwt = null },
            CancellationToken.None);

        var persisted = await dbContext.Set<Vessel>().AsNoTracking().SingleAsync(v => v.Id == vessel.Id);

        Assert.Null(persisted.Dwt);
    }

    [Fact]
    public async Task UpdateAsync_does_not_advance_the_timestamp_for_an_unchanged_request()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId);
        await SetSpecificationsAsync(dbContext, vessel.Id, 40000m);
        var untouched = (await dbContext.Set<Vessel>().AsNoTracking()
            .SingleAsync(v => v.Id == vessel.Id)).UpdatedAtUtc;
        var service = CreateService(dbContext);

        await service.UpdateAsync(
            vessel.Id,
            new UpdateVesselSpecificationsRequest { Dwt = 40000m },
            CancellationToken.None);

        var persisted = await dbContext.Set<Vessel>().AsNoTracking().SingleAsync(v => v.Id == vessel.Id);

        Assert.Equal(untouched, persisted.UpdatedAtUtc);
        Assert.NotEqual(FixedNow.UtcDateTime, persisted.UpdatedAtUtc);
    }

    private static async Task SetSpecificationsAsync(HarborOpsDbContext dbContext, Guid vesselId, decimal dwt)
    {
        var vessel = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vesselId);
        vessel.UpdateSpecifications(
            dwt,
            null,
            null,
            null,
            null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static UpdateVesselSpecificationsService CreateService(HarborOpsDbContext dbContext) =>
        new(
            dbContext,
            new FixedTimeProvider(FixedNow),
            new GetVesselDetailsQuery(dbContext));

    private static async Task<Guid> SeedVesselTypeAsync(HarborOpsDbContext dbContext)
    {
        var vesselType = VesselType.Register("Container Ship", "CNTR", "Container carrying vessel");
        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return vesselType.Id;
    }

    private static async Task<Vessel> SeedVesselAsync(HarborOpsDbContext dbContext, Guid vesselTypeId)
    {
        var vessel = Vessel.Register(Guid.NewGuid(), vesselTypeId, "North Star", "1234567", "Malta", "Active");
        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return vessel;
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
