using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class PlanCargoOperationServiceTests
{
    private static readonly Guid VoyageId = Guid.Parse("7f6e5d4c-3b2a-4190-8f7e-6d5c4b3a2190");
    private static readonly Guid ResponsibleUserId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly DateTime PlannedAtUtc = new(2026, 5, 4, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PlanAsync_plans_the_operation_against_the_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-PLAN-0001");
        var service = new PlanCargoOperationService(dbContext);

        var response = await service.PlanAsync(
            booking.Id,
            ResponsibleUserId,
            Request("Load"),
            CancellationToken.None);

        var operation = await dbContext
            .Set<LoadUnloadOperation>()
            .SingleAsync(CancellationToken.None);
        Assert.Equal(operation.Id, response.Id);
        Assert.Equal(nameof(CargoOperationType.Load), response.OperationType);
        Assert.Equal(nameof(CargoOperationState.Planned), response.OperationState);
        Assert.Equal(booking.Id, operation.CargoBookingId);
        Assert.Equal(PlannedAtUtc, operation.PlannedAtUtc);
    }

    [Fact]
    public async Task PlanAsync_takes_the_actor_id_from_the_caller()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-PLAN-0002");
        var service = new PlanCargoOperationService(dbContext);

        await service.PlanAsync(
            booking.Id,
            ResponsibleUserId,
            Request("Load"),
            CancellationToken.None);

        var operation = await dbContext
            .Set<LoadUnloadOperation>()
            .SingleAsync(CancellationToken.None);
        Assert.Equal(ResponsibleUserId, operation.ResponsibleUserId);
    }

    [Fact]
    public async Task PlanAsync_accepts_an_unload_operation()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-PLAN-0003");
        var service = new PlanCargoOperationService(dbContext);

        var response = await service.PlanAsync(
            booking.Id,
            ResponsibleUserId,
            Request("unload"),
            CancellationToken.None);

        Assert.Equal(nameof(CargoOperationType.Unload), response.OperationType);
    }

    [Fact]
    public async Task PlanAsync_rejects_an_operation_type_outside_the_vocabulary()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-PLAN-0004");
        var service = new PlanCargoOperationService(dbContext);

        var exception = await Assert.ThrowsAsync<PlanCargoOperationValidationException>(
            () => service.PlanAsync(
                booking.Id,
                ResponsibleUserId,
                Request("Inspect"),
                CancellationToken.None));

        Assert.Contains(
            PlanCargoOperationValidator.OperationTypeField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task PlanAsync_rejects_a_missing_operation_type()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-PLAN-0005");
        var service = new PlanCargoOperationService(dbContext);

        var exception = await Assert.ThrowsAsync<PlanCargoOperationValidationException>(
            () => service.PlanAsync(
                booking.Id,
                ResponsibleUserId,
                Request(null),
                CancellationToken.None));

        Assert.Contains(
            PlanCargoOperationValidator.OperationTypeField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task PlanAsync_rejects_a_planned_time_that_is_not_utc()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-PLAN-0006");
        var service = new PlanCargoOperationService(dbContext);
        var localTime = DateTime.SpecifyKind(PlannedAtUtc, DateTimeKind.Local);

        var exception = await Assert.ThrowsAsync<PlanCargoOperationValidationException>(
            () => service.PlanAsync(
                booking.Id,
                ResponsibleUserId,
                Request("Load", localTime),
                CancellationToken.None));

        Assert.Contains(
            PlanCargoOperationValidator.PlannedAtUtcField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task PlanAsync_rejects_a_completed_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-PLAN-0007", CargoStatus.Completed);
        var service = new PlanCargoOperationService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoBookingCompletedException>(
            () => service.PlanAsync(
                booking.Id,
                ResponsibleUserId,
                Request("Load"),
                CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Empty(await dbContext.Set<LoadUnloadOperation>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PlanAsync_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();
        var service = new PlanCargoOperationService(dbContext);
        var unknownBookingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => service.PlanAsync(
                unknownBookingId,
                ResponsibleUserId,
                Request("Load"),
                CancellationToken.None));

        Assert.Equal(unknownBookingId, exception.CargoBookingId);
    }

    private static PlanCargoOperationRequest Request(
        string? operationType,
        DateTime? plannedAtUtc = null)
    {
        return new PlanCargoOperationRequest
        {
            OperationType = operationType,
            PlannedAtUtc = plannedAtUtc ?? PlannedAtUtc,
        };
    }

    private static async Task<CargoBooking> AddBookingAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber,
        CargoStatus status = CargoStatus.Registered)
    {
        var booking = CargoBooking.Register(
            bookingNumber,
            VoyageId,
            "Steel coils",
            "General Cargo",
            5000.000m,
            10,
            isHazardous: false);

        while (booking.Status != status)
        {
            booking.AdvanceStatusTo(booking.Status + 1);
        }

        await dbContext.Set<CargoBooking>().AddAsync(booking, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return booking;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
