using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class ScheduleVoyageServiceTests
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Schedule_normalizes_port_codes_and_creates_planned_voyage()
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleVoyageService(dbContext);
        var request = CreateValidRequest("VOY-001") with
        {
            DeparturePortCode = " merij ",
            ArrivalPortCode = " itgoa ",
        };
        var startedUtc = DateTime.UtcNow;

        var response = await service.ScheduleAsync(request, CancellationToken.None);

        var voyage = await dbContext
            .Set<Voyage>()
            .SingleAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(response.Id, voyage.Id);
        Assert.Equal("MERIJ", voyage.DeparturePortCode);
        Assert.Equal("ITGOA", voyage.ArrivalPortCode);
        Assert.Equal(Voyage.PlannedStatus, voyage.Status);
        Assert.Equal(request.VesselId, voyage.VesselId);
        Assert.Equal("VOY-001", voyage.VoyageNumber);
        Assert.Equal(PlannedDepartureUtc, voyage.PlannedDeparture);
        Assert.Equal(PlannedArrivalUtc, voyage.PlannedArrival);
        Assert.InRange(voyage.CreatedUtc, startedUtc, DateTime.UtcNow);
    }

    [Fact]
    public async Task Schedule_rejects_equal_port_codes()
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleVoyageService(dbContext);
        var request = CreateValidRequest("VOY-002") with
        {
            DeparturePortCode = "MERIJ",
            ArrivalPortCode = " merij ",
        };

        var exception = await Assert.ThrowsAsync<ScheduleVoyageValidationException>(
            () => service.ScheduleAsync(request, CancellationToken.None));

        Assert.Contains(ScheduleVoyageValidator.ArrivalPortCodeField, exception.Errors);
    }

    [Fact]
    public async Task Schedule_rejects_invalid_port_code_lengths()
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleVoyageService(dbContext);
        var request = CreateValidRequest("VOY-003") with
        {
            DeparturePortCode = "MER",
            ArrivalPortCode = "ITGOAX",
        };

        var exception = await Assert.ThrowsAsync<ScheduleVoyageValidationException>(
            () => service.ScheduleAsync(request, CancellationToken.None));

        Assert.Contains(ScheduleVoyageValidator.DeparturePortCodeField, exception.Errors);
        Assert.Contains(ScheduleVoyageValidator.ArrivalPortCodeField, exception.Errors);
    }

    [Fact]
    public async Task Schedule_rejects_non_utc_timestamps()
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleVoyageService(dbContext);
        var request = CreateValidRequest("VOY-004") with
        {
            PlannedDepartureUtc = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Local),
            PlannedArrivalUtc = new DateTime(2026, 9, 4, 17, 30, 0, DateTimeKind.Unspecified),
        };

        var exception = await Assert.ThrowsAsync<ScheduleVoyageValidationException>(
            () => service.ScheduleAsync(request, CancellationToken.None));

        Assert.Contains(ScheduleVoyageValidator.PlannedDepartureUtcField, exception.Errors);
        Assert.Contains(ScheduleVoyageValidator.PlannedArrivalUtcField, exception.Errors);
    }

    [Fact]
    public async Task Schedule_rejects_arrival_that_is_not_later_than_departure()
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleVoyageService(dbContext);
        var request = CreateValidRequest("VOY-005") with
        {
            PlannedArrivalUtc = PlannedDepartureUtc,
        };

        var exception = await Assert.ThrowsAsync<ScheduleVoyageValidationException>(
            () => service.ScheduleAsync(request, CancellationToken.None));

        Assert.Contains(ScheduleVoyageValidator.PlannedArrivalUtcField, exception.Errors);
    }

    [Fact]
    public async Task Schedule_rejects_empty_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleVoyageService(dbContext);
        var request = CreateValidRequest("VOY-006") with
        {
            VesselId = Guid.Empty,
        };

        var exception = await Assert.ThrowsAsync<ScheduleVoyageValidationException>(
            () => service.ScheduleAsync(request, CancellationToken.None));

        Assert.Contains(ScheduleVoyageValidator.VesselIdField, exception.Errors);
    }

    [Fact]
    public async Task Schedule_rejects_missing_required_input()
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleVoyageService(dbContext);
        var request = new ScheduleVoyageRequest
        {
            VoyageNumber = " ",
            VesselId = null,
            DeparturePortCode = null,
            ArrivalPortCode = string.Empty,
            PlannedDepartureUtc = null,
            PlannedArrivalUtc = null,
        };

        var exception = await Assert.ThrowsAsync<ScheduleVoyageValidationException>(
            () => service.ScheduleAsync(request, CancellationToken.None));

        Assert.Equal(6, exception.Errors.Count);
        Assert.Contains(ScheduleVoyageValidator.VoyageNumberField, exception.Errors);
        Assert.Contains(ScheduleVoyageValidator.VesselIdField, exception.Errors);
        Assert.Contains(ScheduleVoyageValidator.DeparturePortCodeField, exception.Errors);
        Assert.Contains(ScheduleVoyageValidator.ArrivalPortCodeField, exception.Errors);
        Assert.Contains(ScheduleVoyageValidator.PlannedDepartureUtcField, exception.Errors);
        Assert.Contains(ScheduleVoyageValidator.PlannedArrivalUtcField, exception.Errors);
    }

    [Fact]
    public async Task Schedule_rejects_duplicate_voyage_number()
    {
        await using var dbContext = CreateDbContext();
        var service = new ScheduleVoyageService(dbContext);
        var firstRequest = CreateValidRequest(" VOY-007 ");
        var duplicateRequest = CreateValidRequest("VOY-007") with
        {
            VesselId = Guid.NewGuid(),
        };

        await service.ScheduleAsync(firstRequest, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateVoyageNumberException>(
            () => service.ScheduleAsync(duplicateRequest, CancellationToken.None));

        Assert.Equal("VOY-007", exception.VoyageNumber);
    }

    private static ScheduleVoyageRequest CreateValidRequest(string voyageNumber)
    {
        return new ScheduleVoyageRequest
        {
            VoyageNumber = voyageNumber,
            VesselId = Guid.NewGuid(),
            DeparturePortCode = "MERIJ",
            ArrivalPortCode = "ITGOA",
            PlannedDepartureUtc = PlannedDepartureUtc,
            PlannedArrivalUtc = PlannedArrivalUtc,
        };
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
