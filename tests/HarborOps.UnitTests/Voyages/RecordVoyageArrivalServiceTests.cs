using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class RecordVoyageArrivalServiceTests
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Arrival_records_utc_value_and_completes_voyage()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedDepartedVoyageAsync(dbContext, "VOY-101");
        var actualArrivalUtc = voyage.CreatedUtc.AddHours(2);
        var service = CreateService(dbContext, voyage.CreatedUtc.AddHours(3));

        var response = await service.RecordArrivalAsync(
            voyage.Id,
            new RecordVoyageArrivalRequest { ActualArrivalUtc = actualArrivalUtc },
            CancellationToken.None);

        var persisted = await dbContext
            .Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyage.Id, CancellationToken.None);

        Assert.Equal(voyage.Id, response.Id);
        Assert.Equal(actualArrivalUtc, response.ActualArrivalUtc);
        Assert.Equal(Voyage.CompletedStatus, response.Status);
        Assert.Equal(actualArrivalUtc, persisted.ActualArrival);
        Assert.Equal(Voyage.CompletedStatus, persisted.Status);
    }

    [Fact]
    public async Task Arrival_rejects_non_utc_value()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedDepartedVoyageAsync(dbContext, "VOY-102");
        var service = CreateService(dbContext, voyage.CreatedUtc.AddHours(3));
        var localArrival = DateTime.SpecifyKind(
            voyage.CreatedUtc.AddHours(2),
            DateTimeKind.Local);

        var exception = await Assert.ThrowsAsync<RecordVoyageArrivalValidationException>(
            () => service.RecordArrivalAsync(
                voyage.Id,
                new RecordVoyageArrivalRequest { ActualArrivalUtc = localArrival },
                CancellationToken.None));

        await AssertVoyageUnchangedAsync(dbContext, voyage.Id);
        Assert.Contains(RecordVoyageArrivalValidator.ActualArrivalUtcField, exception.Errors);
    }

    [Fact]
    public async Task Arrival_rejects_future_value()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedDepartedVoyageAsync(dbContext, "VOY-103");
        var utcNow = voyage.CreatedUtc.AddHours(3);
        var service = CreateService(dbContext, utcNow);

        var exception = await Assert.ThrowsAsync<RecordVoyageArrivalValidationException>(
            () => service.RecordArrivalAsync(
                voyage.Id,
                new RecordVoyageArrivalRequest { ActualArrivalUtc = utcNow.AddMinutes(1) },
                CancellationToken.None));

        await AssertVoyageUnchangedAsync(dbContext, voyage.Id);
        Assert.Contains(RecordVoyageArrivalValidator.ActualArrivalUtcField, exception.Errors);
    }

    [Fact]
    public async Task Arrival_rejects_value_earlier_than_actual_departure()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedDepartedVoyageAsync(dbContext, "VOY-104");
        var service = CreateService(dbContext, voyage.CreatedUtc.AddHours(3));

        var exception = await Assert.ThrowsAsync<RecordVoyageArrivalValidationException>(
            () => service.RecordArrivalAsync(
                voyage.Id,
                new RecordVoyageArrivalRequest
                {
                    ActualArrivalUtc = voyage.ActualDeparture!.Value.AddSeconds(-1),
                },
                CancellationToken.None));

        await AssertVoyageUnchangedAsync(dbContext, voyage.Id);
        Assert.Contains(RecordVoyageArrivalValidator.ActualArrivalUtcField, exception.Errors);
    }

    [Fact]
    public async Task Arrival_rejects_value_equal_to_actual_departure()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedDepartedVoyageAsync(dbContext, "VOY-105");
        var service = CreateService(dbContext, voyage.CreatedUtc.AddHours(3));

        var exception = await Assert.ThrowsAsync<RecordVoyageArrivalValidationException>(
            () => service.RecordArrivalAsync(
                voyage.Id,
                new RecordVoyageArrivalRequest
                {
                    ActualArrivalUtc = voyage.ActualDeparture!.Value,
                },
                CancellationToken.None));

        await AssertVoyageUnchangedAsync(dbContext, voyage.Id);
        Assert.Contains(RecordVoyageArrivalValidator.ActualArrivalUtcField, exception.Errors);
    }

    [Fact]
    public async Task Arrival_rejects_voyage_without_recorded_departure()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedDepartedVoyageAsync(dbContext, "VOY-106");

        dbContext.Entry(voyage).Property(nameof(Voyage.ActualDeparture)).CurrentValue = null;
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = CreateService(dbContext, voyage.CreatedUtc.AddHours(3));

        var exception = await Assert.ThrowsAsync<VoyageNotInProgressException>(
            () => service.RecordArrivalAsync(
                voyage.Id,
                new RecordVoyageArrivalRequest
                {
                    ActualArrivalUtc = voyage.CreatedUtc.AddHours(2),
                },
                CancellationToken.None));

        await AssertVoyageUnchangedAsync(dbContext, voyage.Id);
        Assert.Equal(voyage.Id, exception.VoyageId);
        Assert.Equal(Voyage.InProgressStatus, exception.CurrentStatus);
    }

    [Fact]
    public async Task Arrival_rejects_second_arrival()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedDepartedVoyageAsync(dbContext, "VOY-107");
        var actualArrivalUtc = voyage.CreatedUtc.AddHours(2);
        var service = CreateService(dbContext, voyage.CreatedUtc.AddHours(3));
        var request = new RecordVoyageArrivalRequest { ActualArrivalUtc = actualArrivalUtc };

        await service.RecordArrivalAsync(voyage.Id, request, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<VoyageNotInProgressException>(
            () => service.RecordArrivalAsync(voyage.Id, request, CancellationToken.None));

        var persisted = await dbContext
            .Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyage.Id, CancellationToken.None);

        Assert.Equal(voyage.Id, exception.VoyageId);
        Assert.Equal(Voyage.CompletedStatus, exception.CurrentStatus);
        Assert.Equal(actualArrivalUtc, persisted.ActualArrival);
        Assert.Equal(Voyage.CompletedStatus, persisted.Status);
    }

    [Fact]
    public async Task Arrival_rejects_voyage_that_is_not_in_progress()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedDepartedVoyageAsync(dbContext, "VOY-108");

        dbContext.Entry(voyage).Property(nameof(Voyage.Status)).CurrentValue = "Cancelled";
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = CreateService(dbContext, voyage.CreatedUtc.AddHours(3));

        var exception = await Assert.ThrowsAsync<VoyageNotInProgressException>(
            () => service.RecordArrivalAsync(
                voyage.Id,
                new RecordVoyageArrivalRequest
                {
                    ActualArrivalUtc = voyage.CreatedUtc.AddHours(2),
                },
                CancellationToken.None));

        var persisted = await dbContext
            .Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyage.Id, CancellationToken.None);

        Assert.Equal("Cancelled", exception.CurrentStatus);
        Assert.Null(persisted.ActualArrival);
        Assert.Equal("Cancelled", persisted.Status);
    }

    private static RecordVoyageArrivalService CreateService(
        HarborOpsDbContext dbContext,
        DateTime utcNow)
    {
        return new RecordVoyageArrivalService(
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

        Assert.Null(persisted.ActualArrival);
        Assert.Equal(Voyage.InProgressStatus, persisted.Status);
    }

    private static async Task<Voyage> SeedDepartedVoyageAsync(
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

        voyage.Depart(voyage.CreatedUtc.AddHours(1));

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
