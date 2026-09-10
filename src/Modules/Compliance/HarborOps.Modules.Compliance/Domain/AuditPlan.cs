namespace HarborOps.Modules.Compliance.Domain;

public sealed class AuditPlan
{
    public const string ScheduledStatus = "Scheduled";
    public const string InProgressStatus = "InProgress";
    public const string CompletedStatus = "Completed";
    public const string CancelledStatus = "Cancelled";

    internal const int ScopeMaxLength = 1000;
    internal const int NotesMaxLength = 2000;
    internal const string StartAction = "start";
    internal const string RescheduleAction = "reschedule";
    internal const string AssignAction = "assign";
    internal const string CompleteAction = "complete";
    internal const string CancelAction = "cancel";

    public Guid Id { get; private set; }

    public Guid VesselId { get; private set; }

    public string Type { get; private set; } = null!;

    public string Status { get; private set; } = null!;

    public DateOnly PlannedStartDate { get; private set; }

    public DateOnly DueDate { get; private set; }

    public Guid? AssignedInspectorUserId { get; private set; }

    public string Scope { get; private set; } = null!;

    public string? Notes { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    public int? SurveyAuditId { get; private set; }

    public SurveyAudit? Survey { get; private set; }

    private AuditPlan()
    {
    }

    private AuditPlan(
        Guid id,
        Guid vesselId,
        string type,
        DateOnly plannedStartDate,
        DateOnly dueDate,
        Guid? assignedInspectorUserId,
        string scope,
        string? notes)
    {
        Id = id;
        VesselId = vesselId;
        Type = type;
        Status = ScheduledStatus;
        PlannedStartDate = plannedStartDate;
        DueDate = dueDate;
        AssignedInspectorUserId = assignedInspectorUserId;
        Scope = scope;
        Notes = notes;
    }

    internal static AuditPlan Create(
        Guid vesselId,
        string type,
        DateOnly plannedStartDate,
        DateOnly dueDate,
        Guid? assignedInspectorUserId,
        string scope,
        string? notes)
    {
        return new AuditPlan(
            Guid.NewGuid(),
            vesselId,
            type,
            plannedStartDate,
            dueDate,
            assignedInspectorUserId,
            scope,
            notes);
    }

    internal void Reschedule(DateOnly plannedStartDate, DateOnly dueDate)
    {
        if (Status != ScheduledStatus)
        {
            throw new InvalidAuditPlanTransitionException(Id, Status, RescheduleAction);
        }

        PlannedStartDate = plannedStartDate;
        DueDate = dueDate;
    }

    internal void Assign(Guid inspectorUserId)
    {
        if (Status is not (ScheduledStatus or InProgressStatus))
        {
            throw new InvalidAuditPlanTransitionException(Id, Status, AssignAction);
        }

        AssignedInspectorUserId = inspectorUserId;
    }

    internal void Start(DateTime startedAtUtc)
    {
        if (AssignedInspectorUserId is null || Status != ScheduledStatus)
        {
            throw new InvalidAuditPlanTransitionException(Id, Status, StartAction);
        }

        Status = InProgressStatus;
        StartedAtUtc = startedAtUtc;
    }

    internal void Complete(SurveyAudit survey, DateTime completedAtUtc)
    {
        if (Status != InProgressStatus)
        {
            throw new InvalidAuditPlanTransitionException(Id, Status, CompleteAction);
        }

        Survey = survey;
        Status = CompletedStatus;
        CompletedAtUtc = completedAtUtc;
    }

    internal void Cancel(string reason, DateTime cancelledAtUtc)
    {
        if (Status is not (ScheduledStatus or InProgressStatus))
        {
            throw new InvalidAuditPlanTransitionException(Id, Status, CancelAction);
        }

        Status = CancelledStatus;
        CancelledAtUtc = cancelledAtUtc;
        CancellationReason = reason;
    }
}
