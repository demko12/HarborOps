using HarborOps.Modules.Documents.Domain;

namespace HarborOps.UnitTests.Documents;

public sealed class DocumentRetentionStateTests
{
    private static readonly DateTime FixedNowUtc = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constructor_starts_in_active_state()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());

        Assert.Equal(DocumentRetentionState.ActiveState, state.State);
        Assert.NotEqual(Guid.Empty, state.ConcurrencyToken);
    }

    // ---- Archive ----

    [Fact]
    public void Archive_from_active_moves_to_archived_and_changes_concurrency_token()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        var actorId = Guid.NewGuid();
        var tokenBefore = state.ConcurrencyToken;

        state.Archive(actorId, FixedNowUtc);

        Assert.Equal(DocumentRetentionState.ArchivedState, state.State);
        Assert.Equal(FixedNowUtc, state.ArchivedAtUtc);
        Assert.Equal(actorId, state.ArchivedByUserId);
        Assert.NotEqual(tokenBefore, state.ConcurrencyToken);
    }

    [Fact]
    public void Archive_when_already_archived_throws()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.Archive(Guid.NewGuid(), FixedNowUtc);

        Assert.Throws<InvalidOperationException>(() => state.Archive(Guid.NewGuid(), FixedNowUtc));
    }

    [Fact]
    public void Archive_when_pending_disposition_throws()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.MoveToPendingDisposition(FixedNowUtc);

        Assert.Throws<InvalidOperationException>(() => state.Archive(Guid.NewGuid(), FixedNowUtc));
    }

    [Fact]
    public void Archive_when_disposed_throws()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.MoveToPendingDisposition(FixedNowUtc);
        state.MarkDisposed(FixedNowUtc);

        Assert.Throws<InvalidOperationException>(() => state.Archive(Guid.NewGuid(), FixedNowUtc));
    }

    // ---- Restore ----

    [Fact]
    public void Restore_from_archived_moves_to_active_and_changes_concurrency_token()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.Archive(Guid.NewGuid(), FixedNowUtc);
        var actorId = Guid.NewGuid();
        var tokenBefore = state.ConcurrencyToken;

        state.Restore(actorId, FixedNowUtc);

        Assert.Equal(DocumentRetentionState.ActiveState, state.State);
        Assert.Equal(FixedNowUtc, state.RestoredAtUtc);
        Assert.Equal(actorId, state.RestoredByUserId);
        Assert.NotEqual(tokenBefore, state.ConcurrencyToken);
    }

    [Fact]
    public void Restore_when_active_throws()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => state.Restore(Guid.NewGuid(), FixedNowUtc));
    }

    [Fact]
    public void Restore_when_pending_disposition_throws()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.MoveToPendingDisposition(FixedNowUtc);

        Assert.Throws<InvalidOperationException>(() => state.Restore(Guid.NewGuid(), FixedNowUtc));
    }

    [Fact]
    public void Restore_when_disposed_throws()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.MoveToPendingDisposition(FixedNowUtc);
        state.MarkDisposed(FixedNowUtc);

        Assert.Throws<InvalidOperationException>(() => state.Restore(Guid.NewGuid(), FixedNowUtc));
    }

    // ---- MoveToPendingDisposition ----

    [Fact]
    public void MoveToPendingDisposition_from_active_succeeds_and_changes_concurrency_token()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        var tokenBefore = state.ConcurrencyToken;

        state.MoveToPendingDisposition(FixedNowUtc);

        Assert.Equal(DocumentRetentionState.PendingDispositionState, state.State);
        Assert.NotEqual(tokenBefore, state.ConcurrencyToken);
    }

    [Fact]
    public void MoveToPendingDisposition_from_archived_succeeds()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.Archive(Guid.NewGuid(), FixedNowUtc);

        state.MoveToPendingDisposition(FixedNowUtc);

        Assert.Equal(DocumentRetentionState.PendingDispositionState, state.State);
    }

    [Fact]
    public void MoveToPendingDisposition_when_already_pending_throws()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.MoveToPendingDisposition(FixedNowUtc);

        Assert.Throws<InvalidOperationException>(() => state.MoveToPendingDisposition(FixedNowUtc));
    }

    [Fact]
    public void MoveToPendingDisposition_when_disposed_throws()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.MoveToPendingDisposition(FixedNowUtc);
        state.MarkDisposed(FixedNowUtc);

        Assert.Throws<InvalidOperationException>(() => state.MoveToPendingDisposition(FixedNowUtc));
    }

    // ---- MarkDisposed ----

    [Fact]
    public void MarkDisposed_from_pending_disposition_succeeds_and_changes_concurrency_token()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.MoveToPendingDisposition(FixedNowUtc);
        var tokenBefore = state.ConcurrencyToken;

        state.MarkDisposed(FixedNowUtc);

        Assert.Equal(DocumentRetentionState.DisposedState, state.State);
        Assert.NotEqual(tokenBefore, state.ConcurrencyToken);
    }

    [Fact]
    public void MarkDisposed_when_active_throws()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => state.MarkDisposed(FixedNowUtc));
    }

    [Fact]
    public void MarkDisposed_when_archived_throws()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.Archive(Guid.NewGuid(), FixedNowUtc);

        Assert.Throws<InvalidOperationException>(() => state.MarkDisposed(FixedNowUtc));
    }

    // ---- RevertPendingDisposition ----

    [Fact]
    public void RevertPendingDisposition_from_active_restores_active()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.MoveToPendingDisposition(FixedNowUtc);

        state.RevertPendingDisposition(FixedNowUtc);

        Assert.Equal(DocumentRetentionState.ActiveState, state.State);
    }

    [Fact]
    public void RevertPendingDisposition_from_archived_restores_archived()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.Archive(Guid.NewGuid(), FixedNowUtc);
        state.MoveToPendingDisposition(FixedNowUtc);

        state.RevertPendingDisposition(FixedNowUtc);

        Assert.Equal(DocumentRetentionState.ArchivedState, state.State);
    }

    [Fact]
    public void RevertPendingDisposition_when_not_pending_throws()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => state.RevertPendingDisposition(FixedNowUtc));
    }

    [Fact]
    public void RevertPendingDisposition_then_move_to_pending_again_and_dispose_succeeds()
    {
        var state = new DocumentRetentionState(Guid.NewGuid());
        state.MoveToPendingDisposition(FixedNowUtc);
        state.RevertPendingDisposition(FixedNowUtc);

        state.MoveToPendingDisposition(FixedNowUtc);
        state.MarkDisposed(FixedNowUtc);

        Assert.Equal(DocumentRetentionState.DisposedState, state.State);
    }
}
