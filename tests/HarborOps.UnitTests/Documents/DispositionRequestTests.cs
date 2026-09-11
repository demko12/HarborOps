using HarborOps.Modules.Documents.Domain;

namespace HarborOps.UnitTests.Documents;

public sealed class DispositionRequestTests
{
    private static readonly DateTime FixedNowUtc = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FixedTriggerAtUtc = new(2025, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_sets_all_fields_with_pending_status()
    {
        var documentId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();

        var request = DispositionRequest.Create(documentId, requestedByUserId, "Retention period elapsed.", FixedNowUtc, 365, FixedTriggerAtUtc);

        Assert.NotEqual(Guid.Empty, request.Id);
        Assert.Equal(documentId, request.DocumentId);
        Assert.Equal(requestedByUserId, request.RequestedByUserId);
        Assert.Equal(FixedNowUtc, request.RequestedAtUtc);
        Assert.Equal("Retention period elapsed.", request.Reason);
        Assert.Equal(DispositionRequest.PendingStatus, request.Status);
        Assert.Equal(365, request.SnapshotRetentionPeriodDays);
        Assert.Equal(FixedTriggerAtUtc, request.SnapshotLifecycleTriggerAtUtc);
        Assert.Null(request.DecidedByUserId);
        Assert.Null(request.DecidedAtUtc);
        Assert.Null(request.DecisionReason);
        Assert.NotEqual(Guid.Empty, request.ConcurrencyToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_non_blank_reason(string? reason)
    {
        Assert.Throws<ArgumentException>(() =>
            DispositionRequest.Create(Guid.NewGuid(), Guid.NewGuid(), reason!, FixedNowUtc, 365, FixedTriggerAtUtc));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_non_positive_snapshot_retention_period(int snapshotRetentionPeriodDays)
    {
        Assert.Throws<ArgumentException>(() =>
            DispositionRequest.Create(Guid.NewGuid(), Guid.NewGuid(), "Reason.", FixedNowUtc, snapshotRetentionPeriodDays, FixedTriggerAtUtc));
    }

    // ---- Approve ----

    [Fact]
    public void Approve_by_different_user_sets_approved_and_changes_concurrency_token()
    {
        var request = CreatePendingRequest(out var requestedByUserId);
        var approverId = Guid.NewGuid();
        var tokenBefore = request.ConcurrencyToken;

        request.Approve(approverId, "Looks good.", FixedNowUtc);

        Assert.Equal(DispositionRequest.ApprovedStatus, request.Status);
        Assert.Equal(approverId, request.DecidedByUserId);
        Assert.Equal(FixedNowUtc, request.DecidedAtUtc);
        Assert.Equal("Looks good.", request.DecisionReason);
        Assert.NotEqual(tokenBefore, request.ConcurrencyToken);
        Assert.NotEqual(requestedByUserId, approverId);
    }

    [Fact]
    public void Approve_allows_null_decision_reason()
    {
        var request = CreatePendingRequest(out _);

        request.Approve(Guid.NewGuid(), null, FixedNowUtc);

        Assert.Null(request.DecisionReason);
    }

    [Fact]
    public void Approve_by_requester_throws()
    {
        var request = CreatePendingRequest(out var requestedByUserId);

        Assert.Throws<InvalidOperationException>(() => request.Approve(requestedByUserId, "Approved.", FixedNowUtc));
    }

    [Fact]
    public void Approve_when_already_decided_throws()
    {
        var request = CreatePendingRequest(out _);
        request.Approve(Guid.NewGuid(), "Approved.", FixedNowUtc);

        Assert.Throws<InvalidOperationException>(() => request.Approve(Guid.NewGuid(), "Approved again.", FixedNowUtc));
    }

    // ---- Reject ----

    [Fact]
    public void Reject_by_different_user_sets_rejected_and_changes_concurrency_token()
    {
        var request = CreatePendingRequest(out var requestedByUserId);
        var approverId = Guid.NewGuid();
        var tokenBefore = request.ConcurrencyToken;

        request.Reject(approverId, "Missing approval chain.", FixedNowUtc);

        Assert.Equal(DispositionRequest.RejectedStatus, request.Status);
        Assert.Equal(approverId, request.DecidedByUserId);
        Assert.Equal(FixedNowUtc, request.DecidedAtUtc);
        Assert.Equal("Missing approval chain.", request.DecisionReason);
        Assert.NotEqual(tokenBefore, request.ConcurrencyToken);
        Assert.NotEqual(requestedByUserId, approverId);
    }

    [Fact]
    public void Reject_by_requester_throws()
    {
        var request = CreatePendingRequest(out var requestedByUserId);

        Assert.Throws<InvalidOperationException>(() => request.Reject(requestedByUserId, "Reason.", FixedNowUtc));
    }

    [Fact]
    public void Reject_when_already_decided_throws()
    {
        var request = CreatePendingRequest(out _);
        request.Reject(Guid.NewGuid(), "Reason.", FixedNowUtc);

        Assert.Throws<InvalidOperationException>(() => request.Reject(Guid.NewGuid(), "Reason again.", FixedNowUtc));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_requires_non_blank_decision_reason(string? decisionReason)
    {
        var request = CreatePendingRequest(out _);

        Assert.Throws<ArgumentException>(() => request.Reject(Guid.NewGuid(), decisionReason!, FixedNowUtc));
    }

    private static DispositionRequest CreatePendingRequest(out Guid requestedByUserId)
    {
        requestedByUserId = Guid.NewGuid();
        return DispositionRequest.Create(Guid.NewGuid(), requestedByUserId, "Retention period elapsed.", FixedNowUtc, 365, FixedTriggerAtUtc);
    }
}
