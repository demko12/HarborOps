using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class MaintenanceWorkOrderDomainTests
{
    private static readonly DateTime DueUtc = new(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CompletedUtc = new(2026, 6, 20, 14, 30, 0, DateTimeKind.Utc);

    private static MaintenanceWorkOrder CreateOpen()
    {
        return MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace main engine filter",
            "Filter is due for replacement",
            WorkOrderPriority.High,
            DueUtc);
    }

    [Fact]
    public void A_created_work_order_starts_open_without_a_completion_timestamp()
    {
        var workOrder = CreateOpen();

        Assert.Equal(WorkOrderStatus.Open, workOrder.Status);
        Assert.Null(workOrder.CompletedUtc);
    }

    [Theory]
    [InlineData(
        WorkOrderStatus.Open,
        new[] { WorkOrderAction.Start, WorkOrderAction.Cancel, WorkOrderAction.ReplaceEquipment })]
    [InlineData(
        WorkOrderStatus.InProgress,
        new[] { WorkOrderAction.Cancel, WorkOrderAction.ReplaceEquipment })]
    [InlineData(WorkOrderStatus.Completed, new WorkOrderAction[0])]
    [InlineData(WorkOrderStatus.Canceled, new WorkOrderAction[0])]
    public void The_action_matrix_offers_only_the_supported_moves(
        WorkOrderStatus status,
        WorkOrderAction[] expected)
    {
        Assert.Equal(expected, MaintenanceWorkOrder.AllowedActionsFor(status));
    }

    [Fact]
    public void An_open_work_order_can_start()
    {
        var workOrder = CreateOpen();

        workOrder.Start();

        Assert.Equal(WorkOrderStatus.InProgress, workOrder.Status);
        Assert.Null(workOrder.CompletedUtc);
    }

    [Fact]
    public void An_open_work_order_can_be_cancelled()
    {
        var workOrder = CreateOpen();

        workOrder.Cancel();

        Assert.Equal(WorkOrderStatus.Canceled, workOrder.Status);
        Assert.Null(workOrder.CompletedUtc);
    }

    [Fact]
    public void A_work_order_in_progress_can_be_cancelled()
    {
        var workOrder = CreateOpen();
        workOrder.Start();

        workOrder.Cancel();

        Assert.Equal(WorkOrderStatus.Canceled, workOrder.Status);
    }

    [Fact]
    public void A_work_order_cannot_be_started_twice()
    {
        var workOrder = CreateOpen();
        workOrder.Start();

        var exception = Assert.Throws<WorkOrderTransitionNotAllowedException>(workOrder.Start);

        Assert.Equal(workOrder.Id, exception.WorkOrderId);
        Assert.Equal(WorkOrderStatus.InProgress, exception.CurrentStatus);
        Assert.Equal(WorkOrderStatus.InProgress, exception.RequestedStatus);
    }

    [Fact]
    public void A_completed_work_order_accepts_no_further_transition()
    {
        var workOrder = CreateOpen();
        workOrder.Start();
        workOrder.Complete(CompletedUtc);

        Assert.Throws<WorkOrderTransitionNotAllowedException>(workOrder.Start);
        Assert.Throws<WorkOrderTransitionNotAllowedException>(workOrder.Cancel);
    }

    [Fact]
    public void A_cancelled_work_order_accepts_no_further_transition()
    {
        var workOrder = CreateOpen();
        workOrder.Cancel();

        Assert.Throws<WorkOrderTransitionNotAllowedException>(workOrder.Start);
        Assert.Throws<WorkOrderTransitionNotAllowedException>(workOrder.Cancel);
    }

    [Fact]
    public void A_rejected_transition_leaves_the_status_and_completion_untouched()
    {
        var workOrder = CreateOpen();
        workOrder.Start();
        workOrder.Complete(CompletedUtc);

        Assert.Throws<WorkOrderTransitionNotAllowedException>(workOrder.Cancel);

        Assert.Equal(WorkOrderStatus.Completed, workOrder.Status);
        Assert.Equal(CompletedUtc, workOrder.CompletedUtc);
    }

    [Fact]
    public void An_open_work_order_allows_an_equipment_change()
    {
        var workOrder = CreateOpen();

        workOrder.EnsureEquipmentCanChange();
    }

    [Fact]
    public void A_work_order_in_progress_allows_an_equipment_change()
    {
        var workOrder = CreateOpen();
        workOrder.Start();

        workOrder.EnsureEquipmentCanChange();
    }

    [Fact]
    public void A_completed_work_order_refuses_an_equipment_change()
    {
        var workOrder = CreateOpen();
        workOrder.Start();
        workOrder.Complete(CompletedUtc);

        var exception = Assert.Throws<WorkOrderEquipmentChangeNotAllowedException>(
            workOrder.EnsureEquipmentCanChange);

        Assert.Equal(workOrder.Id, exception.WorkOrderId);
        Assert.Equal(WorkOrderStatus.Completed, exception.CurrentStatus);
    }

    [Fact]
    public void A_cancelled_work_order_refuses_an_equipment_change()
    {
        var workOrder = CreateOpen();
        workOrder.Cancel();

        var exception = Assert.Throws<WorkOrderEquipmentChangeNotAllowedException>(
            workOrder.EnsureEquipmentCanChange);

        Assert.Equal(WorkOrderStatus.Canceled, exception.CurrentStatus);
    }

    [Fact]
    public void Completion_still_records_the_timestamp_from_open_and_in_progress()
    {
        var fromOpen = CreateOpen();
        fromOpen.Complete(CompletedUtc);

        var fromInProgress = CreateOpen();
        fromInProgress.Start();
        fromInProgress.Complete(CompletedUtc);

        Assert.Equal(WorkOrderStatus.Completed, fromOpen.Status);
        Assert.Equal(CompletedUtc, fromOpen.CompletedUtc);
        Assert.Equal(WorkOrderStatus.Completed, fromInProgress.Status);
        Assert.Equal(CompletedUtc, fromInProgress.CompletedUtc);
    }

    [Fact]
    public void A_cancelled_work_order_still_reports_it_cannot_be_completed()
    {
        var workOrder = CreateOpen();
        workOrder.Cancel();

        var exception = Assert.Throws<WorkOrderNotCompletableException>(
            () => workOrder.Complete(CompletedUtc));

        Assert.Equal(WorkOrderStatus.Canceled, exception.CurrentStatus);
    }

    [Theory]
    [InlineData(WorkOrderStatus.Open, true)]
    [InlineData(WorkOrderStatus.InProgress, true)]
    [InlineData(WorkOrderStatus.Completed, false)]
    [InlineData(WorkOrderStatus.Canceled, false)]
    public void Only_open_and_running_work_orders_can_be_overdue(WorkOrderStatus status, bool expected)
    {
        var utcNow = DueUtc.AddDays(1);

        Assert.Equal(expected, MaintenanceWorkOrder.IsOverdue(status, DueUtc, utcNow));
    }

    [Fact]
    public void A_work_order_due_in_the_future_is_not_overdue()
    {
        Assert.False(MaintenanceWorkOrder.IsOverdue(WorkOrderStatus.Open, DueUtc, DueUtc.AddMinutes(-1)));
    }
}
