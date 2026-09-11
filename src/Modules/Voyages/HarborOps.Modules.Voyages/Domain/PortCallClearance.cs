namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallClearance
{
    internal const int StatusMaxLength = 30;

    internal const string PendingStatus = "Pending";
    internal const string CompletedStatus = "Completed";
    internal const string ReopenedStatus = "Reopened";

    private PortCallClearance()
    {
    }

    private PortCallClearance(Guid id, Guid voyageId, Guid voyageStopId, string status)
    {
        Id = id;
        VoyageId = voyageId;
        VoyageStopId = voyageStopId;
        Status = status;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid VoyageId { get; private set; }

    public Guid VoyageStopId { get; private set; }

    public string Status { get; private set; } = null!;

    public Guid? CompletedByUserId { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public int LastEventSequence { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    internal bool AllowsDeparture =>
        string.Equals(Status, CompletedStatus, StringComparison.Ordinal);

    internal static PortCallClearance Create(Guid voyageId, Guid voyageStopId)
    {
        return new PortCallClearance(Guid.NewGuid(), voyageId, voyageStopId, PendingStatus);
    }

    internal (PortCallClearanceItem Item, PortCallClearanceEvent Event) AddItem(
        string category,
        string title,
        bool isRequired,
        DateTime? dueAtUtc,
        string? authorityReference,
        Guid? evidenceDocumentId,
        Guid actorUserId,
        DateTime occurredAtUtc)
    {
        EnsureNotCompleted();

        var item = PortCallClearanceItem.Add(
            Id,
            category,
            title,
            isRequired,
            dueAtUtc,
            authorityReference,
            evidenceDocumentId,
            actorUserId,
            occurredAtUtc);

        Touch();

        var recorded = PortCallClearanceEvent.Record(
            Id,
            item.Id,
            NextSequence(),
            PortCallClearanceEvent.ItemAddedEventType,
            null,
            actorUserId,
            occurredAtUtc);

        return (item, recorded);
    }

    internal PortCallClearanceEvent EditItem(
        PortCallClearanceItem item,
        string category,
        string title,
        bool isRequired,
        DateTime? dueAtUtc,
        string? authorityReference,
        Guid? evidenceDocumentId,
        Guid actorUserId,
        DateTime occurredAtUtc)
    {
        EnsureOwns(item);
        EnsureNotCompleted();

        item.Edit(category, title, isRequired, dueAtUtc, authorityReference, evidenceDocumentId);

        Touch();

        return PortCallClearanceEvent.Record(
            Id,
            item.Id,
            NextSequence(),
            PortCallClearanceEvent.ItemUpdatedEventType,
            null,
            actorUserId,
            occurredAtUtc);
    }

    internal PortCallClearanceEvent ClearItem(
        PortCallClearanceItem item,
        string? authorityReference,
        Guid? evidenceDocumentId,
        Guid actorUserId,
        DateTime occurredAtUtc)
    {
        EnsureOwns(item);

        item.Clear(authorityReference, evidenceDocumentId, actorUserId, occurredAtUtc);

        Touch();

        return PortCallClearanceEvent.Record(
            Id,
            item.Id,
            NextSequence(),
            PortCallClearanceEvent.ItemClearedEventType,
            null,
            actorUserId,
            occurredAtUtc);
    }

    internal PortCallClearanceEvent WaiveItem(
        PortCallClearanceItem item,
        string reason,
        Guid actorUserId,
        DateTime occurredAtUtc)
    {
        EnsureOwns(item);

        item.Waive(reason, actorUserId, occurredAtUtc);

        Touch();

        return PortCallClearanceEvent.Record(
            Id,
            item.Id,
            NextSequence(),
            PortCallClearanceEvent.ItemWaivedEventType,
            reason,
            actorUserId,
            occurredAtUtc);
    }

    internal PortCallClearanceEvent Complete(
        IReadOnlyCollection<PortCallClearanceItem> items,
        Guid actorUserId,
        DateTime occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(items);

        EnsureNotCompleted();

        var requiredCount = items.Count(item => item.IsRequired);
        var unresolvedCount = items.Count(item => item.IsRequired && item.IsPending);

        if (requiredCount == 0 || unresolvedCount > 0)
        {
            throw new PortCallClearanceNotCompletableException(Id, requiredCount, unresolvedCount);
        }

        Status = CompletedStatus;
        CompletedByUserId = actorUserId;
        CompletedAtUtc = occurredAtUtc;

        Touch();

        return PortCallClearanceEvent.Record(
            Id,
            null,
            NextSequence(),
            PortCallClearanceEvent.CompletedEventType,
            null,
            actorUserId,
            occurredAtUtc);
    }

    internal PortCallClearanceEvent Reopen(string reason, Guid actorUserId, DateTime occurredAtUtc)
    {
        if (!string.Equals(Status, CompletedStatus, StringComparison.Ordinal))
        {
            throw new PortCallClearanceNotCompletedException(Id, Status);
        }

        Status = ReopenedStatus;
        CompletedByUserId = null;
        CompletedAtUtc = null;

        Touch();

        return PortCallClearanceEvent.Record(
            Id,
            null,
            NextSequence(),
            PortCallClearanceEvent.ReopenedEventType,
            reason,
            actorUserId,
            occurredAtUtc);
    }

    private int NextSequence() => ++LastEventSequence;

    private void Touch()
    {
        ConcurrencyToken = Guid.NewGuid();
    }

    private void EnsureNotCompleted()
    {
        if (string.Equals(Status, CompletedStatus, StringComparison.Ordinal))
        {
            throw new PortCallClearanceAlreadyCompletedException(Id);
        }
    }

    private void EnsureOwns(PortCallClearanceItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.PortCallClearanceId != Id)
        {
            throw new PortCallClearanceItemNotFoundException(item.Id);
        }
    }
}
