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

    internal IReadOnlyList<DryDockStatus> AllowedNextStatuses => AllowedNextStatusesFor(Status);

    internal static IReadOnlyList<DryDockStatus> AllowedNextStatusesFor(DryDockStatus status)
    {
        return status switch
        {
            DryDockStatus.Planned => [DryDockStatus.InProgress, DryDockStatus.Canceled],
            DryDockStatus.InProgress => [DryDockStatus.Completed],
            _ => [],
        };
    }

    internal static bool IsOverdue(DryDockStatus status, DateOnly startDate, DateOnly today)
    {
        return status == DryDockStatus.Planned && startDate < today;
    }

    internal static int? DaysUntilStart(DryDockStatus status, DateOnly startDate, DateOnly today)
    {
        return status == DryDockStatus.Planned ? startDate.DayNumber - today.DayNumber : null;
    }

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

    internal void Start()
    {
        MoveTo(DryDockStatus.InProgress);
    }

    internal void Complete(DateOnly endDate, decimal actualCost)
    {
        EnsureCanMoveTo(DryDockStatus.Completed);

        if (endDate < StartDate)
        {
            throw new DryDockCompletionInvalidException(
                Id,
                $"the end date '{endDate:yyyy-MM-dd}' is before the start date '{StartDate:yyyy-MM-dd}'.");
        }

        if (actualCost < 0m)
        {
            throw new DryDockCompletionInvalidException(
                Id,
                $"the actual cost '{actualCost}' is negative.");
        }

        Status = DryDockStatus.Completed;
        EndDate = endDate;
        ActualCost = actualCost;
    }

    internal void Cancel()
    {
        MoveTo(DryDockStatus.Canceled);
    }

    private void MoveTo(DryDockStatus requestedStatus)
    {
        EnsureCanMoveTo(requestedStatus);

        Status = requestedStatus;
    }

    private void EnsureCanMoveTo(DryDockStatus requestedStatus)
    {
        if (!AllowedNextStatuses.Contains(requestedStatus))
        {
            throw new DryDockTransitionNotAllowedException(Id, Status, requestedStatus);
        }
    }
}
