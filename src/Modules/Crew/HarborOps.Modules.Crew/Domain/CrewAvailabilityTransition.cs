namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewAvailabilityTransition
{
    internal const int EventTypeMaxLength = 20;
    internal const int StatusMaxLength = 20;
    internal const int ReasonMaxLength = 500;

    internal const string RequestedEvent = "Requested";
    internal const string ApprovedEvent = "Approved";
    internal const string RejectedEvent = "Rejected";
    internal const string CancelledEvent = "Cancelled";

    private CrewAvailabilityTransition()
    {
    }

    private CrewAvailabilityTransition(
        Guid id,
        Guid availabilityRequestId,
        string eventType,
        string? reason,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string previousStatus,
        string newStatus)
    {
        Id = id;
        AvailabilityRequestId = availabilityRequestId;
        EventType = eventType;
        Reason = reason;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
    }

    public Guid Id { get; private set; }
    public Guid AvailabilityRequestId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string? Reason { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string PreviousStatus { get; private set; } = null!;
    public string NewStatus { get; private set; } = null!;

    internal static CrewAvailabilityTransition RecordRequested(
        Guid availabilityRequestId,
        Guid actorUserId,
        DateTime occurredAtUtc)
    {
        return new CrewAvailabilityTransition(
            Guid.NewGuid(),
            availabilityRequestId,
            RequestedEvent,
            reason: null,
            actorUserId,
            occurredAtUtc,
            CrewAvailabilityRequest.RequestedStatus,
            CrewAvailabilityRequest.RequestedStatus);
    }

    internal static CrewAvailabilityTransition RecordApproved(
        Guid availabilityRequestId,
        Guid actorUserId,
        DateTime occurredAtUtc)
    {
        return new CrewAvailabilityTransition(
            Guid.NewGuid(),
            availabilityRequestId,
            ApprovedEvent,
            reason: null,
            actorUserId,
            occurredAtUtc,
            CrewAvailabilityRequest.RequestedStatus,
            CrewAvailabilityRequest.ApprovedStatus);
    }

    internal static CrewAvailabilityTransition RecordRejected(
        Guid availabilityRequestId,
        Guid actorUserId,
        string reason,
        DateTime occurredAtUtc)
    {
        return new CrewAvailabilityTransition(
            Guid.NewGuid(),
            availabilityRequestId,
            RejectedEvent,
            reason,
            actorUserId,
            occurredAtUtc,
            CrewAvailabilityRequest.RequestedStatus,
            CrewAvailabilityRequest.RejectedStatus);
    }

    internal static CrewAvailabilityTransition RecordCancelled(
        Guid availabilityRequestId,
        Guid actorUserId,
        string reason,
        DateTime occurredAtUtc,
        string previousStatus)
    {
        return new CrewAvailabilityTransition(
            Guid.NewGuid(),
            availabilityRequestId,
            CancelledEvent,
            reason,
            actorUserId,
            occurredAtUtc,
            previousStatus,
            CrewAvailabilityRequest.CancelledStatus);
    }
}
