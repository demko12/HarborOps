namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class ScheduleOccurrence
{
    private ScheduleOccurrence()
    {
    }

    private ScheduleOccurrence(
        Guid id,
        Guid scheduleId,
        DateOnly dueDate,
        Guid workOrderId,
        DateTime generatedUtc)
    {
        Id = id;
        ScheduleId = scheduleId;
        DueDate = dueDate;
        WorkOrderId = workOrderId;
        State = ScheduleOccurrenceState.Generated;
        GeneratedUtc = generatedUtc;
    }

    public Guid Id { get; private set; }

    public Guid ScheduleId { get; private set; }

    public DateOnly DueDate { get; private set; }

    public ScheduleOccurrenceState State { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public Guid? ReplacedWorkOrderId { get; private set; }

    public DateTime GeneratedUtc { get; private set; }

    public DateTime? ResolvedUtc { get; private set; }

    internal static ScheduleOccurrence Generate(
        Guid scheduleId,
        DateOnly dueDate,
        Guid workOrderId,
        DateTime generatedUtc)
    {
        return new ScheduleOccurrence(
            Guid.NewGuid(),
            scheduleId,
            dueDate,
            workOrderId,
            generatedUtc);
    }

    internal static bool IsOverdue(ScheduleOccurrenceState state, DateOnly dueDate, DateOnly today)
    {
        return dueDate < today
            && state is ScheduleOccurrenceState.Pending or ScheduleOccurrenceState.Generated;
    }

    internal bool IsLinkedTo(Guid workOrderId)
    {
        return WorkOrderId == workOrderId;
    }

    internal void MarkCompleted(Guid workOrderId, DateTime completedUtc)
    {
        EnsureLinkedTo(workOrderId);
        EnsureState(ScheduleOccurrenceState.Generated, ScheduleOccurrenceState.Completed);

        State = ScheduleOccurrenceState.Completed;
        ResolvedUtc = completedUtc;
    }

    internal void MarkCancelled(Guid workOrderId, DateTime cancelledUtc)
    {
        EnsureLinkedTo(workOrderId);
        EnsureState(ScheduleOccurrenceState.Generated, ScheduleOccurrenceState.Cancelled);

        State = ScheduleOccurrenceState.Cancelled;
        ResolvedUtc = cancelledUtc;
    }

    internal void Replace(Guid workOrderId, DateTime generatedUtc)
    {
        EnsureState(ScheduleOccurrenceState.Cancelled, ScheduleOccurrenceState.Generated);

        ReplacedWorkOrderId = WorkOrderId;
        WorkOrderId = workOrderId;
        State = ScheduleOccurrenceState.Generated;
        GeneratedUtc = generatedUtc;
        ResolvedUtc = null;
    }

    private void EnsureLinkedTo(Guid workOrderId)
    {
        if (WorkOrderId != workOrderId)
        {
            throw new ScheduleOccurrenceLinkInvalidException(Id, WorkOrderId, workOrderId);
        }
    }

    private void EnsureState(ScheduleOccurrenceState required, ScheduleOccurrenceState requested)
    {
        if (State != required)
        {
            throw new ScheduleOccurrenceTransitionNotAllowedException(Id, State, requested);
        }
    }
}
