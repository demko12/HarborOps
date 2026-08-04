using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class DryDock
{
    internal const int PlannedTasksMaxLength = 4000;

    private DryDock()
    {
    }

    private DryDock(
        Guid id,
        Guid vesselId,
        Guid portId,
        DateOnly startDate,
        DateOnly? endDate,
        string plannedTasks,
        decimal? actualCost,
        DryDockStatus status)
    {
        Id = id;
        VesselId = vesselId;
        PortId = portId;
        StartDate = startDate;
        EndDate = endDate;
        PlannedTasks = plannedTasks;
        ActualCost = actualCost;
        Status = status;
    }

    public Guid Id { get; private set; }

    public Guid VesselId { get; private set; }

    public Guid PortId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public string PlannedTasks { get; private set; } = null!;

    public decimal? ActualCost { get; private set; }

    public DryDockStatus Status { get; private set; }

    internal static DryDock Schedule(
        Guid vesselId,
        Guid portId,
        DateOnly startDate,
        string plannedTasks)
    {
        return new DryDock(
            Guid.NewGuid(),
            vesselId,
            portId,
            startDate,
            null,
            plannedTasks,
            null,
            DryDockStatus.Planned);
    }
}
