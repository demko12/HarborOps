using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class GetCargoOperationsQueryTests
{
    private static readonly Guid VoyageId = Guid.Parse("7f6e5d4c-3b2a-4190-8f7e-6d5c4b3a2190");
    private static readonly Guid ResponsibleUserId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly DateTime PlannedAtUtc = new(2026, 5, 4, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StartedAtUtc = new(2026, 5, 4, 7, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime FinishedAtUtc = new(2026, 5, 4, 9, 15, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetOperationsAsync_returns_every_operation_field()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-OPERATIONS-0001");
        var operation = await AddOperationAsync(
            dbContext,
            booking.Id,
            CargoOperationType.Unload,
            PlannedAtUtc);
        var query = new GetCargoOperationsQuery(dbContext);

        var response = await query.GetOperationsAsync(booking.Id, CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(operation.Id, item.OperationId);
        Assert.Equal(nameof(CargoOperationType.Unload), item.OperationType);
        Assert.Equal(PlannedAtUtc, item.PlannedAtUtc);
        Assert.Equal(ResponsibleUserId, item.ResponsibleUserId);
    }

    [Fact]
    public async Task GetOperationsAsync_reports_a_planned_operation()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-OPERATIONS-0002");
        await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load, PlannedAtUtc);
        var query = new GetCargoOperationsQuery(dbContext);

        var response = await query.GetOperationsAsync(booking.Id, CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(nameof(CargoOperationState.Planned), item.OperationState);
        Assert.Null(item.StartedAtUtc);
        Assert.Null(item.FinishedAtUtc);
    }

    [Fact]
    public async Task GetOperationsAsync_reports_a_started_operation_as_in_progress()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-OPERATIONS-0003");
        var operation = await AddOperationAsync(
            dbContext,
            booking.Id,
            CargoOperationType.Load,
            PlannedAtUtc);
        operation.Start(StartedAtUtc);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var query = new GetCargoOperationsQuery(dbContext);

        var response = await query.GetOperationsAsync(booking.Id, CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(nameof(CargoOperationState.InProgress), item.OperationState);
        Assert.Equal(StartedAtUtc, item.StartedAtUtc);
        Assert.Null(item.FinishedAtUtc);
    }

    [Fact]
    public async Task GetOperationsAsync_reports_a_finished_operation_as_completed()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-OPERATIONS-0004");
        var operation = await AddOperationAsync(
            dbContext,
            booking.Id,
            CargoOperationType.Load,
            PlannedAtUtc);
        operation.Start(StartedAtUtc);
        operation.Complete(FinishedAtUtc);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var query = new GetCargoOperationsQuery(dbContext);

        var response = await query.GetOperationsAsync(booking.Id, CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(nameof(CargoOperationState.Completed), item.OperationState);
        Assert.Equal(StartedAtUtc, item.StartedAtUtc);
        Assert.Equal(FinishedAtUtc, item.FinishedAtUtc);
    }

    [Fact]
    public async Task GetOperationsAsync_orders_by_planned_time()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-OPERATIONS-0005");
        await AddOperationAsync(
            dbContext,
            booking.Id,
            CargoOperationType.Unload,
            PlannedAtUtc.AddHours(8));
        await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load, PlannedAtUtc);
        await AddOperationAsync(
            dbContext,
            booking.Id,
            CargoOperationType.Load,
            PlannedAtUtc.AddHours(4));
        var query = new GetCargoOperationsQuery(dbContext);

        var response = await query.GetOperationsAsync(booking.Id, CancellationToken.None);

        Assert.Equal(
            [PlannedAtUtc, PlannedAtUtc.AddHours(4), PlannedAtUtc.AddHours(8)],
            response.Items.Select(item => item.PlannedAtUtc));
    }

    [Fact]
    public async Task GetOperationsAsync_breaks_a_planned_time_tie_by_id()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-OPERATIONS-0006");
        var first = await AddOperationAsync(
            dbContext,
            booking.Id,
            CargoOperationType.Load,
            PlannedAtUtc);
        var second = await AddOperationAsync(
            dbContext,
            booking.Id,
            CargoOperationType.Unload,
            PlannedAtUtc);
        var query = new GetCargoOperationsQuery(dbContext);

        var response = await query.GetOperationsAsync(booking.Id, CancellationToken.None);

        var expected = new[] { first.Id, second.Id }.OrderBy(id => id);
        Assert.Equal(expected, response.Items.Select(item => item.OperationId));
    }

    [Fact]
    public async Task GetOperationsAsync_ignores_operations_of_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-OPERATIONS-0007");
        var otherBooking = await AddBookingAsync(dbContext, "BK-OPERATIONS-0008");
        await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load, PlannedAtUtc);
        await AddOperationAsync(
            dbContext,
            otherBooking.Id,
            CargoOperationType.Unload,
            PlannedAtUtc);
        var query = new GetCargoOperationsQuery(dbContext);

        var response = await query.GetOperationsAsync(booking.Id, CancellationToken.None);

        Assert.Equal(nameof(CargoOperationType.Load), Assert.Single(response.Items).OperationType);
    }

    [Fact]
    public async Task GetOperationsAsync_returns_no_operations_when_none_are_planned()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-OPERATIONS-0009");
        var query = new GetCargoOperationsQuery(dbContext);

        var response = await query.GetOperationsAsync(booking.Id, CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetOperationsAsync_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetCargoOperationsQuery(dbContext);
        var unknownBookingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => query.GetOperationsAsync(unknownBookingId, CancellationToken.None));

        Assert.Equal(unknownBookingId, exception.CargoBookingId);
    }

    private static async Task<CargoBooking> AddBookingAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber)
    {
        var booking = CargoBooking.Register(
            bookingNumber,
            VoyageId,
            "Steel coils",
            "General Cargo",
            5000.000m,
            10,
            isHazardous: false);

        await dbContext.Set<CargoBooking>().AddAsync(booking, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return booking;
    }

    private static async Task<LoadUnloadOperation> AddOperationAsync(
        HarborOpsDbContext dbContext,
        Guid cargoBookingId,
        CargoOperationType operationType,
        DateTime plannedAtUtc)
    {
        var operation = LoadUnloadOperation.Plan(
            cargoBookingId,
            ResponsibleUserId,
            operationType,
            plannedAtUtc);

        await dbContext.Set<LoadUnloadOperation>().AddAsync(operation, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return operation;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
