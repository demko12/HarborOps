namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallActivity
{
    internal const int NotesMaxLength = 1000;
    internal const int StatusMaxLength = 30;
    internal const int MaxActivitiesPerStop = 500;

    internal const string ActiveStatus = "Active";
    internal const string VoidedStatus = "Voided";

    private PortCallActivity()
    {
    }

    private PortCallActivity(
        Guid id,
        Guid voyageId,
        Guid voyageStopId,
        int sequence,
        string activityType,
        DateTime occurredAtUtc,
        DateTime? endedAtUtc,
        string? notes,
        Guid recordedByUserId,
        DateTime recordedAtUtc)
    {
        Id = id;
        VoyageId = voyageId;
        VoyageStopId = voyageStopId;
        Sequence = sequence;
        ActivityType = activityType;
        OccurredAtUtc = occurredAtUtc;
        EndedAtUtc = endedAtUtc;
        Notes = notes;
        Status = ActiveStatus;
        Revision = 1;
        RecordedByUserId = recordedByUserId;
        RecordedAtUtc = recordedAtUtc;
        LastChangedByUserId = recordedByUserId;
        LastChangedAtUtc = recordedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid VoyageId { get; private set; }

    public Guid VoyageStopId { get; private set; }

    public int Sequence { get; private set; }

    public string ActivityType { get; private set; } = null!;

    public DateTime OccurredAtUtc { get; private set; }

    public DateTime? EndedAtUtc { get; private set; }

    public string? Notes { get; private set; }

    public string Status { get; private set; } = null!;

    public int Revision { get; private set; }

    public bool IsLocked { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    public Guid LastChangedByUserId { get; private set; }

    public DateTime LastChangedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    internal bool IsActive => string.Equals(Status, ActiveStatus, StringComparison.Ordinal);

    internal static (PortCallActivity Activity, PortCallActivityRevision Revision) Record(
        Guid voyageId,
        Guid voyageStopId,
        int sequence,
        string activityType,
        DateTime occurredAtUtc,
        DateTime? endedAtUtc,
        string? notes,
        Guid actorUserId,
        string actorName,
        DateTime recordedAtUtc)
    {
        EnsureChronology(occurredAtUtc, endedAtUtc);

        var activity = new PortCallActivity(
            Guid.NewGuid(),
            voyageId,
            voyageStopId,
            sequence,
            activityType,
            occurredAtUtc,
            endedAtUtc,
            notes,
            actorUserId,
            recordedAtUtc);

        return (
            activity,
            PortCallActivityRevision.Capture(
                activity,
                PortCallActivityRevision.RecordedChangeType,
                null,
                actorUserId,
                actorName,
                recordedAtUtc));
    }

    internal PortCallActivityRevision Correct(
        string activityType,
        DateTime occurredAtUtc,
        DateTime? endedAtUtc,
        string? notes,
        string reason,
        Guid actorUserId,
        string actorName,
        DateTime correctedAtUtc)
    {
        EnsureEditable();
        EnsureChronology(occurredAtUtc, endedAtUtc);

        ActivityType = activityType;
        OccurredAtUtc = occurredAtUtc;
        EndedAtUtc = endedAtUtc;
        Notes = notes;

        return Advance(PortCallActivityRevision.CorrectedChangeType, reason, actorUserId, actorName, correctedAtUtc);
    }

    internal PortCallActivityRevision Void(string reason, Guid actorUserId, string actorName, DateTime voidedAtUtc)
    {
        EnsureEditable();

        Status = VoidedStatus;

        return Advance(PortCallActivityRevision.VoidedChangeType, reason, actorUserId, actorName, voidedAtUtc);
    }

    internal bool IsOutsidePortCall(DateTime actualArrivalUtc, DateTime actualDepartureUtc) =>
        OccurredAtUtc < actualArrivalUtc || (EndedAtUtc ?? OccurredAtUtc) > actualDepartureUtc;

    internal void Lock()
    {
        if (IsLocked)
        {
            throw new PortCallActivityLockedException(Id);
        }

        IsLocked = true;
        ConcurrencyToken = Guid.NewGuid();
    }

    private PortCallActivityRevision Advance(
        string changeType,
        string reason,
        Guid actorUserId,
        string actorName,
        DateTime changedAtUtc)
    {
        Revision++;
        LastChangedByUserId = actorUserId;
        LastChangedAtUtc = changedAtUtc;
        ConcurrencyToken = Guid.NewGuid();

        return PortCallActivityRevision.Capture(this, changeType, reason, actorUserId, actorName, changedAtUtc);
    }

    private void EnsureEditable()
    {
        if (IsLocked)
        {
            throw new PortCallActivityLockedException(Id);
        }

        if (!IsActive)
        {
            throw new PortCallActivityVoidedException(Id);
        }
    }

    private static void EnsureChronology(DateTime occurredAtUtc, DateTime? endedAtUtc)
    {
        if (endedAtUtc is { } endedAt && endedAt < occurredAtUtc)
        {
            throw new PortCallActivityChronologyException(occurredAtUtc, endedAt);
        }
    }
}
