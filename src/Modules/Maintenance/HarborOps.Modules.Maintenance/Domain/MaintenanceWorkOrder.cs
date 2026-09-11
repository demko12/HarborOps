using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class MaintenanceWorkOrder
{
    internal const int TitleMaxLength = 200;
    internal const int DescriptionMaxLength = 2000;

    private MaintenanceWorkOrder()
    {
    }

    private MaintenanceWorkOrder(
        Guid id,
        Guid vesselId,
        Guid? dryDockId,
        Guid? assignedEngineerId,
        string title,
        string? description,
        WorkOrderPriority priority,
        WorkOrderStatus status,
        DateTime dueUtc,
        DateTime createdUtc,
        DateTime? completedUtc)
    {
        Id = id;
        VesselId = vesselId;
        DryDockId = dryDockId;
        AssignedEngineerId = assignedEngineerId;
        Title = title;
        Description = description;
        Priority = priority;
        Status = status;
        DueUtc = dueUtc;
        CreatedUtc = createdUtc;
        CompletedUtc = completedUtc;
    }

    public Guid Id { get; private set; }

    public Guid VesselId { get; private set; }

    public Guid? DryDockId { get; private set; }

    public Guid? AssignedEngineerId { get; private set; }

    public string Title { get; private set; } = null!;

    public string? Description { get; private set; }

    public WorkOrderPriority Priority { get; private set; }

    public WorkOrderStatus Status { get; private set; }

    public DateTime DueUtc { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime? CompletedUtc { get; private set; }

    public Guid? SourceScheduleId { get; private set; }

    public DateOnly? SourceOccurrenceDueDate { get; private set; }

    internal static MaintenanceWorkOrder Create(
        Guid vesselId,
        string title,
        string? description,
        WorkOrderPriority priority,
        DateTime dueUtc,
        Guid? dryDockId = null,
        Guid? assignedEngineerId = null)
    {
        return new MaintenanceWorkOrder(
            Guid.NewGuid(),
            vesselId,
            dryDockId,
            assignedEngineerId,
            title,
            description,
            priority,
            WorkOrderStatus.Open,
            dueUtc,
            DateTime.UtcNow,
            null);
    }

    internal static MaintenanceWorkOrder CreateFromSchedule(
        Guid vesselId,
        string title,
        string? description,
        WorkOrderPriority priority,
        Guid scheduleId,
        DateOnly occurrenceDueDate,
        DateTime createdUtc)
    {
        return new MaintenanceWorkOrder(
            Guid.NewGuid(),
            vesselId,
            null,
            null,
            title,
            description,
            priority,
            WorkOrderStatus.Open,
            PreventiveWorkDueTime.ForDueDate(occurrenceDueDate),
            createdUtc,
            null)
        {
            SourceScheduleId = scheduleId,
            SourceOccurrenceDueDate = occurrenceDueDate,
        };
    }

    internal IReadOnlyList<WorkOrderAction> AllowedActions => AllowedActionsFor(Status);

    internal static IReadOnlyList<WorkOrderAction> AllowedActionsFor(WorkOrderStatus status)
    {
        return status switch
        {
            WorkOrderStatus.Open =>
                [WorkOrderAction.Start, WorkOrderAction.Cancel, WorkOrderAction.ReplaceEquipment],
            WorkOrderStatus.InProgress =>
                [WorkOrderAction.Cancel, WorkOrderAction.ReplaceEquipment],
            _ => [],
        };
    }
    internal static bool IsOverdue(WorkOrderStatus status, DateTime dueUtc, DateTime utcNow)
    {
        return dueUtc < utcNow
            && status is WorkOrderStatus.Open or WorkOrderStatus.InProgress;
    }
    internal void Start()
    {
        EnsureAllowed(WorkOrderAction.Start, WorkOrderStatus.InProgress);

        Status = WorkOrderStatus.InProgress;
    }

    internal void Complete(DateTime completedUtc)
    {
        if (Status is not (WorkOrderStatus.Open or WorkOrderStatus.InProgress))
        {
            throw new WorkOrderNotCompletableException(Id, Status);
        }
        Status = WorkOrderStatus.Completed;
        CompletedUtc = completedUtc;
    }

    internal void Cancel()
    {
        EnsureAllowed(WorkOrderAction.Cancel, WorkOrderStatus.Canceled);

        Status = WorkOrderStatus.Canceled;
    }

    internal void EnsureEquipmentCanChange()
    {
        if (!AllowedActions.Contains(WorkOrderAction.ReplaceEquipment))
        {
            throw new WorkOrderEquipmentChangeNotAllowedException(Id, Status);
        }
    }

    private void EnsureAllowed(WorkOrderAction action, WorkOrderStatus requestedStatus)
    {
        if (!AllowedActions.Contains(action))
        {
            throw new WorkOrderTransitionNotAllowedException(Id, Status, requestedStatus);
        }
    }

    internal void EnsureReservationsCanChange()
    {
        if (Status is not WorkOrderStatus.Open)
        {
            throw new WorkOrderReservationChangeNotAllowedException(Id, Status);
        }
    }

    internal void EnsureReservationsCanRelease()
    {
        if (Status is not (WorkOrderStatus.Open or WorkOrderStatus.Canceled))
        {
            throw new WorkOrderReservationReleaseNotAllowedException(Id, Status);
        }
    }
}
