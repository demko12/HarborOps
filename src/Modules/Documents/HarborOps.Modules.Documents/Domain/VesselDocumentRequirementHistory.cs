namespace HarborOps.Modules.Documents.Domain;

internal sealed class VesselDocumentRequirementHistory
{
    internal const int EventTypeMaxLength = 20;
    internal const int ReasonMaxLength = 2000;

    internal const string CreatedEvent = "Created";
    internal const string UpdatedEvent = "Updated";
    internal const string DisabledEvent = "Disabled";

    private VesselDocumentRequirementHistory()
    {
    }

    private VesselDocumentRequirementHistory(
        Guid id,
        VesselDocumentRequirement requirement,
        string eventType,
        string? reason)
    {
        Id = id;
        RequirementId = requirement.Id;
        Revision = requirement.Revision;
        EventType = eventType;
        Reason = reason;
        IsEnabled = requirement.IsEnabled;
        MinimumRemainingValidityDays = requirement.MinimumRemainingValidityDays;
        Instruction = requirement.Instruction;
        ActorUserId = requirement.UpdatedByUserId;
        OccurredAtUtc = requirement.UpdatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid RequirementId { get; private set; }
    public int Revision { get; private set; }
    public string EventType { get; private set; } = null!;
    public string? Reason { get; private set; }
    public bool IsEnabled { get; private set; }
    public int MinimumRemainingValidityDays { get; private set; }
    public string? Instruction { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    internal static VesselDocumentRequirementHistory RecordCreated(VesselDocumentRequirement requirement, string reason)
    {
        return new VesselDocumentRequirementHistory(Guid.NewGuid(), requirement, CreatedEvent, RequireReason(reason));
    }

    internal static VesselDocumentRequirementHistory RecordUpdated(VesselDocumentRequirement requirement, string reason)
    {
        return new VesselDocumentRequirementHistory(Guid.NewGuid(), requirement, UpdatedEvent, RequireReason(reason));
    }

    internal static VesselDocumentRequirementHistory RecordDisabled(VesselDocumentRequirement requirement, string reason)
    {
        return new VesselDocumentRequirementHistory(Guid.NewGuid(), requirement, DisabledEvent, RequireReason(reason));
    }

    private static string RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required.", nameof(reason));
        }

        if (reason.Length > ReasonMaxLength)
        {
            throw new ArgumentException($"Reason must not exceed {ReasonMaxLength} characters.", nameof(reason));
        }

        return reason;
    }
}
