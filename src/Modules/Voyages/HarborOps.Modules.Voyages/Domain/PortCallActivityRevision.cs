namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallActivityRevision
{
    internal const int ChangeTypeMaxLength = 30;
    internal const int ReasonMaxLength = 200;
    internal const int ActorNameMaxLength = 256;

    internal const string RecordedChangeType = "Recorded";
    internal const string CorrectedChangeType = "Corrected";
    internal const string VoidedChangeType = "Voided";

    private PortCallActivityRevision()
    {
    }

    private PortCallActivityRevision(
        Guid id,
        Guid portCallActivityId,
        int revision,
        string changeType,
        string activityType,
        DateTime occurredAtUtc,
        DateTime? endedAtUtc,
        string? notes,
        string? reason,
        Guid actorUserId,
        string actorName,
        DateTime changedAtUtc)
    {
        Id = id;
        PortCallActivityId = portCallActivityId;
        Revision = revision;
        ChangeType = changeType;
        ActivityType = activityType;
        OccurredAtUtc = occurredAtUtc;
        EndedAtUtc = endedAtUtc;
        Notes = notes;
        Reason = reason;
        ActorUserId = actorUserId;
        ActorName = actorName;
        ChangedAtUtc = changedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid PortCallActivityId { get; private set; }

    public int Revision { get; private set; }

    public string ChangeType { get; private set; } = null!;

    public string ActivityType { get; private set; } = null!;

    public DateTime OccurredAtUtc { get; private set; }

    public DateTime? EndedAtUtc { get; private set; }

    public string? Notes { get; private set; }

    public string? Reason { get; private set; }

    public Guid ActorUserId { get; private set; }

    public string ActorName { get; private set; } = null!;

    public DateTime ChangedAtUtc { get; private set; }

    internal static PortCallActivityRevision Capture(
        PortCallActivity activity,
        string changeType,
        string? reason,
        Guid actorUserId,
        string actorName,
        DateTime changedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);

        return new PortCallActivityRevision(
            Guid.NewGuid(),
            activity.Id,
            activity.Revision,
            changeType,
            activity.ActivityType,
            activity.OccurredAtUtc,
            activity.EndedAtUtc,
            activity.Notes,
            reason,
            actorUserId,
            actorName,
            changedAtUtc);
    }
}
