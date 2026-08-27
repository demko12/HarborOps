using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.UnitTests.Cargo;

public sealed class LoadUnloadOperationTests
{
    private static readonly Guid CargoBookingId = Guid.Parse("2c1b0a99-8877-4655-b433-2211ffeeddcc");
    private static readonly Guid ResponsibleUserId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly DateTime PlannedAtUtc = new(2026, 5, 4, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StartedAtUtc = new(2026, 5, 4, 7, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime FinishedAtUtc = new(2026, 5, 4, 9, 15, 0, DateTimeKind.Utc);

    [Fact]
    public void Plan_starts_the_operation_as_planned()
    {
        var operation = PlanOperation();

        Assert.NotEqual(Guid.Empty, operation.Id);
        Assert.Equal(CargoBookingId, operation.CargoBookingId);
        Assert.Equal(ResponsibleUserId, operation.ResponsibleUserId);
        Assert.Equal(CargoOperationType.Load, operation.OperationType);
        Assert.Equal(CargoStatus.Planned, operation.Status);
        Assert.Equal(PlannedAtUtc, operation.PlannedAtUtc);
        Assert.Null(operation.StartedAtUtc);
        Assert.Null(operation.FinishedAtUtc);
        Assert.Equal(CargoOperationState.Planned, operation.State);
    }

    [Fact]
    public void Start_marks_the_operation_in_progress_without_finishing_it()
    {
        var operation = PlanOperation();

        operation.Start(StartedAtUtc);

        Assert.Equal(StartedAtUtc, operation.StartedAtUtc);
        Assert.Null(operation.FinishedAtUtc);
        Assert.Equal(CargoOperationState.InProgress, operation.State);
    }

    [Fact]
    public void Start_leaves_the_shared_status_untouched()
    {
        var operation = PlanOperation();

        operation.Start(StartedAtUtc);

        Assert.Equal(CargoStatus.Planned, operation.Status);
    }

    [Fact]
    public void Start_rejects_an_operation_that_already_started()
    {
        var operation = PlanOperation();
        operation.Start(StartedAtUtc);

        var exception = Assert.Throws<LoadUnloadOperationTransitionNotAllowedException>(
            () => operation.Start(FinishedAtUtc));

        Assert.Equal(operation.Id, exception.OperationId);
        Assert.Equal(CargoOperationState.InProgress, exception.CurrentState);
        Assert.Equal(
            LoadUnloadOperationTransitionNotAllowedException.StartAction,
            exception.RequestedAction);
        Assert.Equal(StartedAtUtc, operation.StartedAtUtc);
    }

    [Fact]
    public void Start_rejects_a_completed_operation()
    {
        var operation = PlanOperation();
        operation.Start(StartedAtUtc);
        operation.Complete(FinishedAtUtc);

        var exception = Assert.Throws<LoadUnloadOperationTransitionNotAllowedException>(
            () => operation.Start(FinishedAtUtc));

        Assert.Equal(CargoOperationState.Completed, exception.CurrentState);
    }

    [Fact]
    public void Complete_finishes_a_started_load_operation()
    {
        var operation = PlanOperation();
        operation.Start(StartedAtUtc);

        operation.Complete(FinishedAtUtc);

        Assert.Equal(StartedAtUtc, operation.StartedAtUtc);
        Assert.Equal(FinishedAtUtc, operation.FinishedAtUtc);
        Assert.Equal(CargoStatus.Loaded, operation.Status);
        Assert.Equal(CargoOperationState.Completed, operation.State);
    }

    [Fact]
    public void Complete_finishes_a_started_unload_operation()
    {
        var operation = PlanOperation(CargoOperationType.Unload);
        operation.Start(StartedAtUtc);

        operation.Complete(FinishedAtUtc);

        Assert.Equal(CargoStatus.Unloaded, operation.Status);
        Assert.Equal(CargoOperationState.Completed, operation.State);
    }

    [Fact]
    public void Complete_rejects_an_operation_that_never_started()
    {
        var operation = PlanOperation();

        var exception = Assert.Throws<LoadUnloadOperationTransitionNotAllowedException>(
            () => operation.Complete(FinishedAtUtc));

        Assert.Equal(CargoOperationState.Planned, exception.CurrentState);
        Assert.Equal(
            LoadUnloadOperationTransitionNotAllowedException.CompleteAction,
            exception.RequestedAction);
        Assert.Null(operation.FinishedAtUtc);
        Assert.Equal(CargoStatus.Planned, operation.Status);
    }

    [Fact]
    public void Complete_rejects_a_repeated_completion()
    {
        var operation = PlanOperation();
        operation.Start(StartedAtUtc);
        operation.Complete(FinishedAtUtc);
        var repeatedUtc = FinishedAtUtc.AddHours(1);

        var exception = Assert.Throws<LoadUnloadOperationTransitionNotAllowedException>(
            () => operation.Complete(repeatedUtc));

        Assert.Equal(CargoOperationState.Completed, exception.CurrentState);
        Assert.Equal(FinishedAtUtc, operation.FinishedAtUtc);
    }

    [Fact]
    public void StateFor_reads_no_timestamps_as_planned()
    {
        Assert.Equal(CargoOperationState.Planned, LoadUnloadOperation.StateFor(null, null));
    }

    [Fact]
    public void StateFor_reads_a_start_without_a_finish_as_in_progress()
    {
        Assert.Equal(
            CargoOperationState.InProgress,
            LoadUnloadOperation.StateFor(StartedAtUtc, null));
    }

    [Fact]
    public void StateFor_reads_a_finish_as_completed()
    {
        Assert.Equal(
            CargoOperationState.Completed,
            LoadUnloadOperation.StateFor(StartedAtUtc, FinishedAtUtc));
    }

    [Fact]
    public void OutcomeStatusFor_maps_a_load_to_loaded()
    {
        Assert.Equal(
            CargoStatus.Loaded,
            LoadUnloadOperation.OutcomeStatusFor(CargoOperationType.Load));
    }

    [Fact]
    public void OutcomeStatusFor_maps_an_unload_to_unloaded()
    {
        Assert.Equal(
            CargoStatus.Unloaded,
            LoadUnloadOperation.OutcomeStatusFor(CargoOperationType.Unload));
    }

    private static LoadUnloadOperation PlanOperation(
        CargoOperationType operationType = CargoOperationType.Load)
    {
        return LoadUnloadOperation.Plan(
            CargoBookingId,
            ResponsibleUserId,
            operationType,
            PlannedAtUtc);
    }
}
