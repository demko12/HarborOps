namespace HarborOps.Modules.Documents.Domain;

public sealed class DocumentRetentionState
{
    public const string ActiveState = "Active";
    public const string ArchivedState = "Archived";
    public const string PendingDispositionState = "PendingDisposition";
    public const string DisposedState = "Disposed";

    internal const int StateMaxLength = 50;

    public Guid DocumentId { get; private set; }

    public string State { get; private set; } = string.Empty;

    public string? PreDispositionState { get; private set; }

    public DateTime? ArchivedAtUtc { get; private set; }

    public Guid? ArchivedByUserId { get; private set; }

    public DateTime? RestoredAtUtc { get; private set; }

    public Guid? RestoredByUserId { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    private DocumentRetentionState()
    {
    }

    public DocumentRetentionState(Guid documentId)
    {
        DocumentId = documentId;
        State = ActiveState;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Archive(Guid actorId, DateTime nowUtc)
    {
        if (!string.Equals(State, ActiveState, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only an active document can be archived.");
        }

        State = ArchivedState;
        ArchivedAtUtc = nowUtc;
        ArchivedByUserId = actorId;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Restore(Guid actorId, DateTime nowUtc)
    {
        if (!string.Equals(State, ArchivedState, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only an archived document can be restored.");
        }

        State = ActiveState;
        RestoredAtUtc = nowUtc;
        RestoredByUserId = actorId;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void MoveToPendingDisposition(DateTime nowUtc)
    {
        if (State is not (ActiveState or ArchivedState))
        {
            throw new InvalidOperationException("A disposition request can only be created while the document is Active or Archived.");
        }

        PreDispositionState = State;
        State = PendingDispositionState;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void MarkDisposed(DateTime nowUtc)
    {
        if (!string.Equals(State, PendingDispositionState, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only a document pending disposition can be disposed.");
        }

        State = DisposedState;
        PreDispositionState = null;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void RevertPendingDisposition(DateTime nowUtc)
    {
        if (!string.Equals(State, PendingDispositionState, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only a document pending disposition can be reverted.");
        }

        State = PreDispositionState ?? ActiveState;
        PreDispositionState = null;
        ConcurrencyToken = Guid.NewGuid();
    }
}
