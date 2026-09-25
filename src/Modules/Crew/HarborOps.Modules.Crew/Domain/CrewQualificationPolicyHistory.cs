namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewQualificationPolicyHistory
{
    internal const int EventTypeMaxLength = 20;
    internal const int RequiredTypesSnapshotMaxLength = 4000;

    internal const string CreatedEvent = "Created";
    internal const string UpdatedEvent = "Updated";

    private CrewQualificationPolicyHistory()
    {
    }

    private CrewQualificationPolicyHistory(
        Guid id,
        Guid policyId,
        string eventType,
        Guid actorUserId,
        DateTime occurredAtUtc,
        bool? previousEnabled,
        bool? newEnabled,
        string? previousRequiredTypesSnapshot,
        string? newRequiredTypesSnapshot)
    {
        Id = id;
        PolicyId = policyId;
        EventType = eventType;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
        PreviousEnabled = previousEnabled;
        NewEnabled = newEnabled;
        PreviousRequiredTypesSnapshot = previousRequiredTypesSnapshot;
        NewRequiredTypesSnapshot = newRequiredTypesSnapshot;
    }

    public Guid Id { get; private set; }
    public Guid PolicyId { get; private set; }
    public string EventType { get; private set; } = null!;
    public Guid ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public bool? PreviousEnabled { get; private set; }
    public bool? NewEnabled { get; private set; }
    public string? PreviousRequiredTypesSnapshot { get; private set; }
    public string? NewRequiredTypesSnapshot { get; private set; }

    internal static CrewQualificationPolicyHistory RecordCreated(
        Guid policyId, Guid actorUserId, DateTime occurredAtUtc, bool enabled, string newRequiredTypesSnapshot)
    {
        return new CrewQualificationPolicyHistory(
            Guid.NewGuid(), policyId, CreatedEvent, actorUserId, occurredAtUtc,
            previousEnabled: null, newEnabled: enabled,
            previousRequiredTypesSnapshot: null, newRequiredTypesSnapshot);
    }

    internal static CrewQualificationPolicyHistory RecordUpdated(
        Guid policyId,
        Guid actorUserId,
        DateTime occurredAtUtc,
        bool previousEnabled,
        bool newEnabled,
        string previousRequiredTypesSnapshot,
        string newRequiredTypesSnapshot)
    {
        return new CrewQualificationPolicyHistory(
            Guid.NewGuid(), policyId, UpdatedEvent, actorUserId, occurredAtUtc,
            previousEnabled, newEnabled, previousRequiredTypesSnapshot, newRequiredTypesSnapshot);
    }
}
