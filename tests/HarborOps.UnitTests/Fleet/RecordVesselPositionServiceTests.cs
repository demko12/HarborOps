using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Fleet;

public sealed class RecordVesselPositionServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(-90, 180)]
    [InlineData(90, -180)]
    public async Task RecordAsync_accepts_boundary_coordinates(double latitude, double longitude)
    {
        await using var dbContext = CreateDbContext();
        var vessel = await SeedVesselAsync(dbContext);
        var service = CreateService(dbContext);

        var response = await service.RecordAsync(
            vessel.Id,
            new RecordVesselPositionRequest
            {
                Latitude = (decimal)latitude,
                Longitude = (decimal)longitude,
            },
            CancellationToken.None);

        Assert.Equal(vessel.Id, response.VesselId);
        Assert.Equal((decimal)latitude, response.Latitude);
        Assert.Equal((decimal)longitude, response.Longitude);
    }

    [Theory]
    [InlineData(90.000001, 0)]
    [InlineData(-90.000001, 0)]
    [InlineData(91, 0)]
    [InlineData(1000, 0)]
    public async Task RecordAsync_rejects_latitude_outside_the_allowed_range(
        double latitude,
        double longitude)
    {
        await using var dbContext = CreateDbContext();
        var vessel = await SeedVesselAsync(dbContext);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<RecordVesselPositionValidationException>(
            () => service.RecordAsync(
                vessel.Id,
                new RecordVesselPositionRequest
                {
                    Latitude = (decimal)latitude,
                    Longitude = (decimal)longitude,
                },
                CancellationToken.None));

        Assert.Contains(RecordVesselPositionValidator.LatitudeField, exception.Errors);
        Assert.DoesNotContain(RecordVesselPositionValidator.LongitudeField, exception.Errors);
    }

    [Theory]
    [InlineData(0, 180.000001)]
    [InlineData(0, -180.000001)]
    [InlineData(0, 181)]
    [InlineData(0, -1000)]
    public async Task RecordAsync_rejects_longitude_outside_the_allowed_range(
        double latitude,
        double longitude)
    {
        await using var dbContext = CreateDbContext();
        var vessel = await SeedVesselAsync(dbContext);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<RecordVesselPositionValidationException>(
            () => service.RecordAsync(
                vessel.Id,
                new RecordVesselPositionRequest
                {
                    Latitude = (decimal)latitude,
                    Longitude = (decimal)longitude,
                },
                CancellationToken.None));

        Assert.Contains(RecordVesselPositionValidator.LongitudeField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_rejects_missing_coordinates()
    {
        await using var dbContext = CreateDbContext();
        var vessel = await SeedVesselAsync(dbContext);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<RecordVesselPositionValidationException>(
            () => service.RecordAsync(
                vessel.Id,
                new RecordVesselPositionRequest(),
                CancellationToken.None));

        Assert.Contains(RecordVesselPositionValidator.LatitudeField, exception.Errors);
        Assert.Contains(RecordVesselPositionValidator.LongitudeField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_validates_before_checking_vessel_existence()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<RecordVesselPositionValidationException>(
            () => service.RecordAsync(
                Guid.NewGuid(),
                new RecordVesselPositionRequest { Latitude = 200m, Longitude = 0m },
                CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_throws_for_an_unknown_vessel()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var unknownVesselId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VesselNotFoundException>(
            () => service.RecordAsync(
                unknownVesselId,
                new RecordVesselPositionRequest { Latitude = 10m, Longitude = 20m },
                CancellationToken.None));

        Assert.Equal(unknownVesselId, exception.VesselId);
    }

    [Fact]
    public async Task RecordAsync_assigns_the_observation_time_from_the_server_clock()
    {
        await using var dbContext = CreateDbContext();
        var vessel = await SeedVesselAsync(dbContext);
        var service = CreateService(dbContext);

        var response = await service.RecordAsync(
            vessel.Id,
            new RecordVesselPositionRequest { Latitude = 35.5m, Longitude = -5.25m },
            CancellationToken.None);

        Assert.Equal(FixedNow.UtcDateTime, response.ReportedAtUtc);
        Assert.Equal(FixedNow.UtcDateTime, vessel.LastPositionReportedAtUtc);
        Assert.Equal(DateTimeKind.Utc, response.ReportedAtUtc.Kind);
    }

    [Fact]
    public async Task RecordAsync_persists_the_position()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid vesselId;

        await using (var dbContext = CreateDbContext(databaseName))
        {
            var vessel = await SeedVesselAsync(dbContext);
            vesselId = vessel.Id;

            await CreateService(dbContext).RecordAsync(
                vesselId,
                new RecordVesselPositionRequest { Latitude = 35.898765m, Longitude = -5.123456m },
                CancellationToken.None);
        }

        await using var verificationContext = CreateDbContext(databaseName);
        var persisted = await verificationContext.Set<Vessel>()
            .AsNoTracking()
            .SingleAsync(v => v.Id == vesselId);

        Assert.Equal(35.898765m, persisted.CurrentLatitude);
        Assert.Equal(-5.123456m, persisted.CurrentLongitude);
        Assert.Equal(FixedNow.UtcDateTime, persisted.LastPositionReportedAtUtc);
    }

    [Fact]
    public async Task RecordAsync_overwrites_the_previous_position()
    {
        await using var dbContext = CreateDbContext();
        var vessel = await SeedVesselAsync(dbContext);
        var service = CreateService(dbContext);

        await service.RecordAsync(
            vessel.Id,
            new RecordVesselPositionRequest { Latitude = 10m, Longitude = 20m },
            CancellationToken.None);
        await service.RecordAsync(
            vessel.Id,
            new RecordVesselPositionRequest { Latitude = 11m, Longitude = 21m },
            CancellationToken.None);

        Assert.Equal(11m, vessel.CurrentLatitude);
        Assert.Equal(21m, vessel.CurrentLongitude);
    }

    [Fact]
    public async Task A_status_change_does_not_refresh_the_position_report_time()
    {
        await using var dbContext = CreateDbContext();
        var vessel = await SeedVesselAsync(dbContext);

        await CreateService(dbContext).RecordAsync(
            vessel.Id,
            new RecordVesselPositionRequest { Latitude = 10m, Longitude = 20m },
            CancellationToken.None);

        await new RecordVesselStatusChangeService(dbContext).RecordAsync(
            vessel.Id,
            "operator@harborops.test",
            new RecordVesselStatusChangeRequest { Status = "En Route" },
            CancellationToken.None);

        Assert.Equal("En Route", vessel.CurrentStatus);
        Assert.Equal(FixedNow.UtcDateTime, vessel.LastPositionReportedAtUtc);
        Assert.NotEqual(vessel.LastPositionReportedAtUtc, vessel.UpdatedAtUtc);
    }

    [Fact]
    public async Task A_specification_change_does_not_refresh_the_position_report_time()
    {
        await using var dbContext = CreateDbContext();
        var vessel = await SeedVesselAsync(dbContext);

        await CreateService(dbContext).RecordAsync(
            vessel.Id,
            new RecordVesselPositionRequest { Latitude = 10m, Longitude = 20m },
            CancellationToken.None);

        vessel.UpdateSpecifications(50_000m, 30_000m, 250m, 32m, 12m);
        await dbContext.SaveChangesAsync();

        Assert.Equal(FixedNow.UtcDateTime, vessel.LastPositionReportedAtUtc);
        Assert.Equal(50_000m, vessel.Dwt);
    }

    private static RecordVesselPositionService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedNow));

    private static async Task<Vessel> SeedVesselAsync(HarborOpsDbContext dbContext)
    {
        var vesselType = VesselType.Register("Container Ship", "CNTR", "Container carrying vessel");
        await dbContext.Set<VesselType>().AddAsync(vesselType);

        var vessel = Vessel.Register(
            Guid.NewGuid(),
            vesselType.Id,
            "North Star",
            "1000001",
            "Malta",
            "Active");

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }

    private static HarborOpsDbContext CreateDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
