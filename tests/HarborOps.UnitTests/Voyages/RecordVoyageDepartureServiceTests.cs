using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class RecordVoyageDepartureServiceTests
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Departure_records_utc_value_and_moves_voyage_into_progress()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-001");
        var actualDepartureUtc = voyage.CreatedUtc.AddHours(1);
        var service = CreateService(dbContext, voyage.CreatedUtc.AddHours(2));

        var response = await service.RecordDepartureAsync(
            voyage.Id,
            new RecordVoyageDepartureRequest { ActualDepartureUtc = actualDepartureUtc },
            CancellationToken.None);

        var persisted = await dbContext
            .Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyage.Id, CancellationToken.None);

        Assert.Equal(voyage.Id, response.Id);
        Assert.Equal(actualDepartureUtc, response.ActualDepartureUtc);
        Assert.Equal(Voyage.InProgressStatus, response.Status);
        Assert.Equal(actualDepartureUtc, persisted.ActualDeparture);
        Assert.Equal(Voyage.InProgressStatus, persisted.Status);
    }

    [Fact]
    public async Task Departure_rejects_non_utc_value()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-002");
        var service = CreateService(dbContext, voyage.CreatedUtc.AddHours(2));
        var localDeparture = DateTime.SpecifyKind(
            voyage.CreatedUtc.AddHours(1),
            DateTimeKind.Local);

        var exception = await Assert.ThrowsAsync<RecordVoyageDepartureValidationException>(
            () => service.RecordDepartureAsync(
                voyage.Id,
                new RecordVoyageDepartureRequest { ActualDepartureUtc = localDeparture },
                CancellationToken.None));

        await AssertVoyageUnchangedAsync(dbContext, voyage.Id);
        Assert.Contains(RecordVoyageDepartureValidator.ActualDepartureUtcField, exception.Errors);
    }

    [Fact]
    public async Task Departure_rejects_future_value()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-003");
        var utcNow = voyage.CreatedUtc.AddHours(2);
        var service = CreateService(dbContext, utcNow);

        var exception = await Assert.ThrowsAsync<RecordVoyageDepartureValidationException>(
            () => service.RecordDepartureAsync(
                voyage.Id,
                new RecordVoyageDepartureRequest { ActualDepartureUtc = utcNow.AddMinutes(1) },
                CancellationToken.None));

        await AssertVoyageUnchangedAsync(dbContext, voyage.Id);
        Assert.Contains(RecordVoyageDepartureValidator.ActualDepartureUtcField, exception.Errors);
    }

    [Fact]
    public async Task Departure_rejects_value_earlier_than_created_utc()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-004");
        var service = CreateService(dbContext, voyage.CreatedUtc.AddHours(2));

        var exception = await Assert.ThrowsAsync<RecordVoyageDepartureValidationException>(
            () => service.RecordDepartureAsync(
                voyage.Id,
                new RecordVoyageDepartureRequest
                {
                    ActualDepartureUtc = voyage.CreatedUtc.AddSeconds(-1),
                },
                CancellationToken.None));

        await AssertVoyageUnchangedAsync(dbContext, voyage.Id);
        Assert.Contains(RecordVoyageDepartureValidator.ActualDepartureUtcField, exception.Errors);
    }

    [Fact]
    public async Task Departure_rejects_second_departure()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-005");
        var actualDepartureUtc = voyage.CreatedUtc.AddHours(1);
        var service = CreateService(dbContext, voyage.CreatedUtc.AddHours(2));
        var request = new RecordVoyageDepartureRequest
        {
            ActualDepartureUtc = actualDepartureUtc,
        };

        await service.RecordDepartureAsync(voyage.Id, request, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<VoyageNotPlannedException>(
            () => service.RecordDepartureAsync(voyage.Id, request, CancellationToken.None));

        var persisted = await dbContext
            .Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyage.Id, CancellationToken.None);

        Assert.Equal(voyage.Id, exception.VoyageId);
        Assert.Equal(Voyage.InProgressStatus, exception.CurrentStatus);
        Assert.Equal(actualDepartureUtc, persisted.ActualDeparture);
        Assert.Equal(Voyage.InProgressStatus, persisted.Status);
    }

    [Fact]
    public async Task Departure_rejects_voyage_that_is_not_planned()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-006");

        dbContext.Entry(voyage).Property(nameof(Voyage.Status)).CurrentValue = "Cancelled";
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = CreateService(dbContext, voyage.CreatedUtc.AddHours(2));

        var exception = await Assert.ThrowsAsync<VoyageNotPlannedException>(
            () => service.RecordDepartureAsync(
                voyage.Id,
                new RecordVoyageDepartureRequest
                {
                    ActualDepartureUtc = voyage.CreatedUtc.AddHours(1),
                },
                CancellationToken.None));

        var persisted = await dbContext
            .Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyage.Id, CancellationToken.None);

        Assert.Equal("Cancelled", exception.CurrentStatus);
        Assert.Null(persisted.ActualDeparture);
        Assert.Equal("Cancelled", persisted.Status);
    }

    [Fact]
    public async Task Departure_rejects_unknown_voyage()
    {
        await using var dbContext = CreateDbContext();
        var utcNow = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);
        var service = CreateService(dbContext, utcNow);
        var unknownVoyageId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VoyageNotFoundException>(
            () => service.RecordDepartureAsync(
                unknownVoyageId,
                new RecordVoyageDepartureRequest { ActualDepartureUtc = utcNow.AddHours(-1) },
                CancellationToken.None));

        Assert.Equal(unknownVoyageId, exception.VoyageId);
    }

    private static RecordVoyageDepartureService CreateService(
        HarborOpsDbContext dbContext,
        DateTime utcNow)
    {
        return new RecordVoyageDepartureService(
            dbContext,
            new FixedTimeProvider(new DateTimeOffset(utcNow)));
    }

    private static async Task AssertVoyageUnchangedAsync(
        HarborOpsDbContext dbContext,
        Guid voyageId)
    {
        var persisted = await dbContext
            .Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyageId, CancellationToken.None);

        Assert.Null(persisted.ActualDeparture);
        Assert.Equal(Voyage.PlannedStatus, persisted.Status);
    }

    private static async Task<Voyage> SeedPlannedVoyageAsync(
        HarborOpsDbContext dbContext,
        string voyageNumber)
    {
        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MERIJ",
            "ITGOA",
            PlannedDepartureUtc,
            PlannedArrivalUtc);

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.SaveChangesAsync();

        return voyage;
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
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
