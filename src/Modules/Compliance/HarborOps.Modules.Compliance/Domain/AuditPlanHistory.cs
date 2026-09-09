namespace HarborOps.Modules.Compliance.Domain;

public sealed class AuditPlanHistory
{
    internal const string RescheduledEvent = "Rescheduled";
    internal const string AssignedEvent = "Assigned";
    internal const string StartedEvent = "Started";
    internal const string CompletedEvent = "Completed";
    internal const string CancelledEvent = "Cancelled";

    internal const int EventTypeMaxLength = 30;
    internal const int ReasonMaxLength = 500;

    public Guid Id { get; private set; }

    public Guid AuditPlanId { get; private set; }

    public string EventType { get; private set; } = null!;

    public string? Reason { get; private set; }

    public Guid ActorUserId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public string PreviousStatus { get; private set; } = null!;

    public string NewStatus { get; private set; } = null!;

    public DateOnly? PreviousPlannedStartDate { get; private set; }

    public DateOnly? NewPlannedStartDate { get; private set; }

    public DateOnly? PreviousDueDate { get; private set; }

    public DateOnly? NewDueDate { get; private set; }

    public Guid? PreviousInspectorUserId { get; private set; }

    public Guid? NewInspectorUserId { get; private set; }

    private AuditPlanHistory()
    {
    }

    private AuditPlanHistory(
        Guid id,
        Guid auditPlanId,
        string eventType,
        string? reason,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string previousStatus,
        string newStatus,
        DateOnly? previousPlannedStartDate,
        DateOnly? newPlannedStartDate,
        DateOnly? previousDueDate,
        DateOnly? newDueDate,
        Guid? previousInspectorUserId,
        Guid? newInspectorUserId)
    {
        Id = id;
        AuditPlanId = auditPlanId;
        EventType = eventType;
        Reason = reason;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        PreviousPlannedStartDate = previousPlannedStartDate;
        NewPlannedStartDate = newPlannedStartDate;
        PreviousDueDate = previousDueDate;
        NewDueDate = newDueDate;
        PreviousInspectorUserId = previousInspectorUserId;
        NewInspectorUserId = newInspectorUserId;
    }

    internal static AuditPlanHistory RecordRescheduled(
        Guid auditPlanId,
        string reason,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string status,
        DateOnly previousPlannedStartDate,
        DateOnly newPlannedStartDate,
        DateOnly previousDueDate,
        DateOnly newDueDate)
    {
        return new AuditPlanHistory(
            Guid.NewGuid(),
            auditPlanId,
            RescheduledEvent,
            reason,
            actorUserId,
            occurredAtUtc,
            status,
            status,
            previousPlannedStartDate,
            newPlannedStartDate,
            previousDueDate,
            newDueDate,
            previousInspectorUserId: null,
            newInspectorUserId: null);
    }

    internal static AuditPlanHistory RecordAssigned(
        Guid auditPlanId,
        string? reason,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string status,
        Guid? previousInspectorUserId,
        Guid newInspectorUserId)
    {
        return new AuditPlanHistory(
            Guid.NewGuid(),
            auditPlanId,
            AssignedEvent,
            reason,
            actorUserId,
            occurredAtUtc,
            status,
            status,
            previousPlannedStartDate: null,
            newPlannedStartDate: null,
            previousDueDate: null,
            newDueDate: null,
            previousInspectorUserId,
            newInspectorUserId);
    }

    internal static AuditPlanHistory RecordStarted(
        Guid auditPlanId,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string previousStatus,
        string newStatus)
    {
        return new AuditPlanHistory(
            Guid.NewGuid(),
            auditPlanId,
            StartedEvent,
            reason: null,
            actorUserId,
            occurredAtUtc,
            previousStatus,
            newStatus,
            previousPlannedStartDate: null,
            newPlannedStartDate: null,
            previousDueDate: null,
            newDueDate: null,
            previousInspectorUserId: null,
            newInspectorUserId: null);
    }

    internal static AuditPlanHistory RecordCompleted(
        Guid auditPlanId,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string previousStatus,
        string newStatus)
    {
        return new AuditPlanHistory(
            Guid.NewGuid(),
            auditPlanId,
            CompletedEvent,
            reason: null,
            actorUserId,
            occurredAtUtc,
            previousStatus,
            newStatus,
            previousPlannedStartDate: null,
            newPlannedStartDate: null,
            previousDueDate: null,
            newDueDate: null,
            previousInspectorUserId: null,
            newInspectorUserId: null);
    }

    internal static AuditPlanHistory RecordCancelled(
        Guid auditPlanId,
        string reason,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string previousStatus,
        string newStatus)
    {
        return new AuditPlanHistory(
            Guid.NewGuid(),
            auditPlanId,
            CancelledEvent,
            reason,
            actorUserId,
            occurredAtUtc,
            previousStatus,
            newStatus,
            previousPlannedStartDate: null,
            newPlannedStartDate: null,
            previousDueDate: null,
            newDueDate: null,
            previousInspectorUserId: null,
            newInspectorUserId: null);
    }
}
