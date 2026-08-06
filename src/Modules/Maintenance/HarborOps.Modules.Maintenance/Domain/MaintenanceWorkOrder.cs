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
}
