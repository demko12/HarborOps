using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class ExecuteCargoOperationServiceTests
{
    private static readonly Guid VoyageId = Guid.Parse("7f6e5d4c-3b2a-4190-8f7e-6d5c4b3a2190");
    private static readonly Guid ResponsibleUserId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly DateTime PlannedAtUtc = new(2026, 5, 4, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StartedAtUtc = new(2026, 5, 4, 7, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime FinishedAtUtc = new(2026, 5, 4, 9, 15, 0, DateTimeKind.Utc);

    [Fact]
    public async Task StartAsync_marks_the_operation_in_progress()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0001");
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");

        var response = await CreateService(dbContext, StartedAtUtc)
            .StartAsync(operation.Id, CancellationToken.None);

        Assert.Equal(operation.Id, response.OperationId);
        Assert.Equal(nameof(CargoOperationState.InProgress), response.OperationState);
        Assert.Equal(StartedAtUtc, response.StartedAtUtc);
        Assert.Null(response.FinishedAtUtc);
    }

    [Fact]
    public async Task StartAsync_takes_the_timestamp_from_the_time_provider()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0002");
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");

        await CreateService(dbContext, StartedAtUtc).StartAsync(operation.Id, CancellationToken.None);

        Assert.Equal(StartedAtUtc, operation.StartedAtUtc);
        Assert.Null(operation.FinishedAtUtc);
    }

    [Fact]
    public async Task StartAsync_rejects_a_repeated_start()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0003");
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        await CreateService(dbContext, StartedAtUtc).StartAsync(operation.Id, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CargoOperationTransitionException>(
            () => CreateService(dbContext, FinishedAtUtc)
                .StartAsync(operation.Id, CancellationToken.None));

        Assert.Equal(operation.Id, exception.OperationId);
        Assert.Equal(CargoOperationState.InProgress, exception.CurrentState);
        Assert.Equal(StartedAtUtc, operation.StartedAtUtc);
    }

    [Fact]
    public async Task StartAsync_rejects_an_unknown_operation()
    {
        await using var dbContext = CreateDbContext();
        var unknownOperationId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<CargoOperationNotFoundException>(
            () => CreateService(dbContext, StartedAtUtc)
                .StartAsync(unknownOperationId, CancellationToken.None));

        Assert.Equal(unknownOperationId, exception.OperationId);
    }

    [Fact]
    public async Task StartAsync_rejects_a_load_before_the_cargo_is_planned()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0017", CargoStatus.Registered);
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");

        var exception = await Assert.ThrowsAsync<CargoOperationBookingPhaseException>(
            () => CreateService(dbContext, StartedAtUtc)
                .StartAsync(operation.Id, CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Equal(CargoStatus.Registered, exception.CurrentStatus);
        Assert.Equal(CargoStatus.Planned, exception.RequiredStatus);
        Assert.Null(operation.StartedAtUtc);
    }

    [Fact]
    public async Task StartAsync_rejects_a_load_while_the_cargo_is_in_transit()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0018", CargoStatus.InTransit);
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567", CargoStatus.Loaded);

        var exception = await Assert.ThrowsAsync<CargoOperationBookingPhaseException>(
            () => CreateService(dbContext, StartedAtUtc)
                .StartAsync(operation.Id, CancellationToken.None));

        Assert.Equal(CargoOperationType.Load, exception.OperationType);
        Assert.Equal(CargoStatus.InTransit, exception.CurrentStatus);
        Assert.Null(operation.StartedAtUtc);
    }

    [Fact]
    public async Task StartAsync_rejects_an_unload_before_the_cargo_arrives()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0019", CargoStatus.InTransit);
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Unload);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567", CargoStatus.Loaded);

        var exception = await Assert.ThrowsAsync<CargoOperationBookingPhaseException>(
            () => CreateService(dbContext, StartedAtUtc)
                .StartAsync(operation.Id, CancellationToken.None));

        Assert.Equal(CargoOperationType.Unload, exception.OperationType);
        Assert.Equal(CargoStatus.Arrived, exception.RequiredStatus);
        Assert.Null(operation.StartedAtUtc);
    }

    [Fact]
    public async Task StartAsync_rejects_a_booking_without_containers()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0020");
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);

        var exception = await Assert.ThrowsAsync<CargoManifestEmptyException>(
            () => CreateService(dbContext, StartedAtUtc)
                .StartAsync(operation.Id, CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Null(operation.StartedAtUtc);
    }

    [Fact]
    public async Task StartAsync_rejects_an_unload_when_the_containers_are_not_loaded()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0021", CargoStatus.Arrived);
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Unload);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");

        var exception = await Assert.ThrowsAsync<CargoManifestNotReadyException>(
            () => CreateService(dbContext, StartedAtUtc)
                .StartAsync(operation.Id, CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Equal(CargoStatus.Loaded, exception.RequiredContainerStatus);
        Assert.Null(operation.StartedAtUtc);
    }

    [Fact]
    public async Task StartAsync_rejects_a_load_when_the_containers_are_already_loaded()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0022");
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567", CargoStatus.Loaded);

        var exception = await Assert.ThrowsAsync<CargoManifestNotReadyException>(
            () => CreateService(dbContext, StartedAtUtc)
                .StartAsync(operation.Id, CancellationToken.None));

        Assert.Equal(CargoOperationType.Load, exception.OperationType);
        Assert.Equal(CargoStatus.Registered, exception.RequiredContainerStatus);
        Assert.Null(operation.StartedAtUtc);
    }

    [Fact]
    public async Task CompleteAsync_rejects_an_operation_that_never_started()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0004");
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");

        var exception = await Assert.ThrowsAsync<CargoOperationTransitionException>(
            () => CreateService(dbContext, FinishedAtUtc)
                .CompleteAsync(operation.Id, CancellationToken.None));

        Assert.Equal(CargoOperationState.Planned, exception.CurrentState);
        Assert.Null(operation.FinishedAtUtc);
    }

    [Fact]
    public async Task CompleteAsync_loads_the_operation_and_every_booking_container()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0005");
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        await AddContainerAsync(dbContext, booking.Id, "TGHU7654321");
        await CreateService(dbContext, StartedAtUtc).StartAsync(operation.Id, CancellationToken.None);

        var response = await CreateService(dbContext, FinishedAtUtc)
            .CompleteAsync(operation.Id, CancellationToken.None);

        Assert.Equal(nameof(CargoOperationState.Completed), response.OperationState);
        Assert.Equal(FinishedAtUtc, response.FinishedAtUtc);
        Assert.Equal(CargoStatus.Loaded, operation.Status);
        Assert.All(
            await dbContext.Set<Container>().ToListAsync(CancellationToken.None),
            container => Assert.Equal(CargoStatus.Loaded, container.Status));
    }

    [Fact]
    public async Task CompleteAsync_unloads_every_booking_container_for_an_unload()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0006", CargoStatus.Arrived);
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Unload);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567", CargoStatus.Loaded);
        await CreateService(dbContext, StartedAtUtc).StartAsync(operation.Id, CancellationToken.None);

        await CreateService(dbContext, FinishedAtUtc)
            .CompleteAsync(operation.Id, CancellationToken.None);

        Assert.Equal(CargoStatus.Unloaded, operation.Status);
        Assert.All(
            await dbContext.Set<Container>().ToListAsync(CancellationToken.None),
            container => Assert.Equal(CargoStatus.Unloaded, container.Status));
    }

    [Fact]
    public async Task CompleteAsync_leaves_containers_of_another_booking_untouched()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0007");
        var otherBooking = await AddBookingAsync(dbContext, "BK-EXECUTE-0008");
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        var otherContainer = await AddContainerAsync(dbContext, otherBooking.Id, "TGHU7654321");
        await CreateService(dbContext, StartedAtUtc).StartAsync(operation.Id, CancellationToken.None);

        await CreateService(dbContext, FinishedAtUtc)
            .CompleteAsync(operation.Id, CancellationToken.None);

        Assert.Equal(CargoStatus.Registered, otherContainer.Status);
    }

    [Fact]
    public async Task CompleteAsync_leaves_the_booking_status_to_the_tracking_workflow()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0009");
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        await CreateService(dbContext, StartedAtUtc).StartAsync(operation.Id, CancellationToken.None);

        await CreateService(dbContext, FinishedAtUtc)
            .CompleteAsync(operation.Id, CancellationToken.None);

        Assert.Equal(CargoStatus.Planned, booking.Status);
    }

    [Fact]
    public async Task CompleteAsync_rejects_a_repeated_completion()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0010");
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "TGHU7654321");
        await CreateService(dbContext, StartedAtUtc).StartAsync(operation.Id, CancellationToken.None);
        await CreateService(dbContext, FinishedAtUtc)
            .CompleteAsync(operation.Id, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CargoOperationTransitionException>(
            () => CreateService(dbContext, FinishedAtUtc.AddHours(1))
                .CompleteAsync(operation.Id, CancellationToken.None));

        Assert.Equal(CargoOperationState.Completed, exception.CurrentState);
        Assert.Equal(FinishedAtUtc, operation.FinishedAtUtc);
    }

    [Fact]
    public async Task CompleteAsync_rejects_an_unknown_operation()
    {
        await using var dbContext = CreateDbContext();
        var unknownOperationId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<CargoOperationNotFoundException>(
            () => CreateService(dbContext, FinishedAtUtc)
                .CompleteAsync(unknownOperationId, CancellationToken.None));

        Assert.Equal(unknownOperationId, exception.OperationId);
    }

    [Fact]
    public async Task CompleteAsync_leaves_the_operation_and_containers_unchanged_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid operationId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            var booking = await AddBookingAsync(seedContext, "BK-EXECUTE-0011");
            var operation = await AddOperationAsync(seedContext, booking.Id, CargoOperationType.Load);
            await AddContainerAsync(seedContext, booking.Id, "MSKU1234567");
            await AddContainerAsync(seedContext, booking.Id, "TGHU7654321");
            operation.Start(StartedAtUtc);
            await seedContext.SaveChangesAsync(CancellationToken.None);
            operationId = operation.Id;
        }

        await using (var failingContext = CreateDbContext(databaseName, failOnSave: true))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => CreateService(failingContext, FinishedAtUtc)
                    .CompleteAsync(operationId, CancellationToken.None));
        }

        await using var assertContext = CreateDbContext(databaseName);
        var savedOperation = await assertContext
            .Set<LoadUnloadOperation>()
            .SingleAsync(CancellationToken.None);
        Assert.Equal(CargoOperationState.InProgress, savedOperation.State);
        Assert.Null(savedOperation.FinishedAtUtc);
        Assert.All(
            await assertContext.Set<Container>().ToListAsync(CancellationToken.None),
            container => Assert.Equal(CargoStatus.Registered, container.Status));
    }

    [Fact]
    public async Task StartAsync_leaves_the_operation_unstarted_when_the_manifest_is_empty()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid operationId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            var booking = await AddBookingAsync(seedContext, "BK-EXECUTE-0012");
            var operation = await AddOperationAsync(seedContext, booking.Id, CargoOperationType.Load);
            operationId = operation.Id;
        }

        await using (var actContext = CreateDbContext(databaseName))
        {
            await Assert.ThrowsAsync<CargoManifestEmptyException>(
                () => CreateService(actContext, StartedAtUtc)
                    .StartAsync(operationId, CancellationToken.None));
        }

        await using var assertContext = CreateDbContext(databaseName);
        var savedOperation = await assertContext
            .Set<LoadUnloadOperation>()
            .SingleAsync(CancellationToken.None);
        Assert.Equal(CargoOperationState.Planned, savedOperation.State);
        Assert.Null(savedOperation.StartedAtUtc);
    }

    [Fact]
    public async Task StartAsync_rejects_a_second_operation_while_one_is_in_progress()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0013");
        var runningOperation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        var plannedOperation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        await CreateService(dbContext, StartedAtUtc)
            .StartAsync(runningOperation.Id, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CargoOperationAlreadyInProgressException>(
            () => CreateService(dbContext, FinishedAtUtc)
                .StartAsync(plannedOperation.Id, CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Null(plannedOperation.StartedAtUtc);
        Assert.Equal(CargoOperationState.Planned, plannedOperation.State);
    }

    [Fact]
    public async Task StartAsync_allows_the_unload_once_the_cargo_has_arrived()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-EXECUTE-0014");
        var loadOperation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        var unloadOperation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Unload);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        await CreateService(dbContext, StartedAtUtc)
            .StartAsync(loadOperation.Id, CancellationToken.None);
        await CreateService(dbContext, FinishedAtUtc)
            .CompleteAsync(loadOperation.Id, CancellationToken.None);
        await AdvanceBookingToAsync(dbContext, booking, CargoStatus.Arrived);

        var response = await CreateService(dbContext, FinishedAtUtc)
            .StartAsync(unloadOperation.Id, CancellationToken.None);

        Assert.Equal(nameof(CargoOperationState.InProgress), response.OperationState);
        Assert.Equal(FinishedAtUtc, unloadOperation.StartedAtUtc);
    }

    [Fact]
    public async Task StartAsync_ignores_an_operation_in_progress_on_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var runningBooking = await AddBookingAsync(dbContext, "BK-EXECUTE-0015");
        var ownBooking = await AddBookingAsync(dbContext, "BK-EXECUTE-0016");
        var runningOperation = await AddOperationAsync(
            dbContext,
            runningBooking.Id,
            CargoOperationType.Load);
        var ownOperation = await AddOperationAsync(dbContext, ownBooking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, runningBooking.Id, "MSKU1234567");
        await AddContainerAsync(dbContext, ownBooking.Id, "TGHU7654321");
        await CreateService(dbContext, StartedAtUtc)
            .StartAsync(runningOperation.Id, CancellationToken.None);

        var response = await CreateService(dbContext, FinishedAtUtc)
            .StartAsync(ownOperation.Id, CancellationToken.None);

        Assert.Equal(nameof(CargoOperationState.InProgress), response.OperationState);
        Assert.Equal(FinishedAtUtc, ownOperation.StartedAtUtc);
    }

    [Fact]
    public async Task StartAsync_rejects_loading_hazardous_cargo_that_was_never_declared()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(
            dbContext,
            "BK-EXECUTE-HAZ-1",
            isHazardous: true);
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");

        var exception = await Assert.ThrowsAsync<CargoSafetyApprovalRequiredException>(
            () => CreateService(dbContext, StartedAtUtc)
                .StartAsync(operation.Id, CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Null(exception.ReviewStatus);
        Assert.Null(operation.StartedAtUtc);
    }

    [Theory]
    [InlineData(nameof(CargoSafetyReviewStatus.Draft))]
    [InlineData(nameof(CargoSafetyReviewStatus.Submitted))]
    [InlineData(nameof(CargoSafetyReviewStatus.Rejected))]
    public async Task StartAsync_rejects_loading_hazardous_cargo_before_its_declaration_is_approved(
        string reviewStatusName)
    {
        var reviewStatus = Enum.Parse<CargoSafetyReviewStatus>(reviewStatusName);

        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(
            dbContext,
            "BK-EXECUTE-HAZ-2",
            isHazardous: true);
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        await AddDeclarationAsync(dbContext, booking.Id, reviewStatus);

        var exception = await Assert.ThrowsAsync<CargoSafetyApprovalRequiredException>(
            () => CreateService(dbContext, StartedAtUtc)
                .StartAsync(operation.Id, CancellationToken.None));

        Assert.Equal(reviewStatus, exception.ReviewStatus);
        Assert.Null(operation.StartedAtUtc);
    }

    [Fact]
    public async Task StartAsync_loads_hazardous_cargo_once_its_declaration_is_approved()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(
            dbContext,
            "BK-EXECUTE-HAZ-3",
            isHazardous: true);
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Load);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567");
        await AddDeclarationAsync(dbContext, booking.Id, CargoSafetyReviewStatus.Approved);

        var response = await CreateService(dbContext, StartedAtUtc)
            .StartAsync(operation.Id, CancellationToken.None);

        Assert.Equal(nameof(CargoOperationState.InProgress), response.OperationState);
        Assert.Equal(StartedAtUtc, operation.StartedAtUtc);
    }

    [Fact]
    public async Task StartAsync_does_not_require_an_approved_declaration_to_unload()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(
            dbContext,
            "BK-EXECUTE-HAZ-4",
            CargoStatus.Arrived,
            isHazardous: true);
        var operation = await AddOperationAsync(dbContext, booking.Id, CargoOperationType.Unload);
        await AddContainerAsync(dbContext, booking.Id, "MSKU1234567", CargoStatus.Loaded);

        var response = await CreateService(dbContext, StartedAtUtc)
            .StartAsync(operation.Id, CancellationToken.None);

        Assert.Equal(nameof(CargoOperationState.InProgress), response.OperationState);
    }

    private static async Task AddDeclarationAsync(
        HarborOpsDbContext dbContext,
        Guid cargoBookingId,
        CargoSafetyReviewStatus reviewStatus)
    {
        var declaration = CargoSafetyInfo.Declare(
            cargoBookingId,
            "3",
            "UN1203",
            "Keep below 25C",
            handlingNotes: null);

        if (reviewStatus is not CargoSafetyReviewStatus.Draft)
        {
            declaration.Submit(ResponsibleUserId, PlannedAtUtc);
        }

        if (reviewStatus is CargoSafetyReviewStatus.Approved)
        {
            declaration.Approve(ResponsibleUserId, PlannedAtUtc, notes: null);
        }

        if (reviewStatus is CargoSafetyReviewStatus.Rejected)
        {
            declaration.Reject(ResponsibleUserId, PlannedAtUtc, "Storage conditions are unclear.");
        }

        await dbContext.Set<CargoSafetyInfo>().AddAsync(declaration, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static ExecuteCargoOperationService CreateService(
        HarborOpsDbContext dbContext,
        DateTime utcNow)
    {
        return new ExecuteCargoOperationService(dbContext, new FixedTimeProvider(utcNow));
    }

    private static async Task<CargoBooking> AddBookingAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber,
        CargoStatus status = CargoStatus.Planned,
        bool isHazardous = false)
    {
        var booking = CargoBooking.Register(
            bookingNumber,
            VoyageId,
            "Steel coils",
            "General Cargo",
            5000.000m,
            10,
            isHazardous);

        await dbContext.Set<CargoBooking>().AddAsync(booking, CancellationToken.None);
        await AdvanceBookingToAsync(dbContext, booking, status);

        return booking;
    }

    private static async Task AdvanceBookingToAsync(
        HarborOpsDbContext dbContext,
        CargoBooking booking,
        CargoStatus status)
    {
        while (booking.Status != status)
        {
            booking.AdvanceStatusTo(booking.Status + 1);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<LoadUnloadOperation> AddOperationAsync(
        HarborOpsDbContext dbContext,
        Guid cargoBookingId,
        CargoOperationType operationType)
    {
        var operation = LoadUnloadOperation.Plan(
            cargoBookingId,
            ResponsibleUserId,
            operationType,
            PlannedAtUtc);

        await dbContext.Set<LoadUnloadOperation>().AddAsync(operation, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return operation;
    }

    private static async Task<Container> AddContainerAsync(
        HarborOpsDbContext dbContext,
        Guid cargoBookingId,
        string containerNumber,
        CargoStatus status = CargoStatus.Registered)
    {
        var container = Container.Register(
            containerNumber,
            "20GP",
            status,
            vesselId: null,
            cargoBookingId: cargoBookingId);

        await dbContext.Set<Container>().AddAsync(container, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return container;
    }

    private static HarborOpsDbContext CreateDbContext(
        string? databaseName = null,
        bool failOnSave = false)
    {
        var builder = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}");

        if (failOnSave)
        {
            builder.AddInterceptors(new FailingSaveChangesInterceptor());
        }

        return new HarborOpsDbContext(builder.Options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }

    private sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException("The save was rejected.");
        }
    }
}
