using HarborOps.Modules.Documents.Domain;

namespace HarborOps.UnitTests.Documents;

public sealed class DocumentReviewWorkflowTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    // ---- SubmitForReview ----

    [Fact]
    public void SubmitForReview_moves_draft_with_file_to_pending_review_and_changes_concurrency_token()
    {
        var document = CreateDraftDocumentWithFile();
        var tokenBefore = document.ConcurrencyToken;

        document.SubmitForReview(Guid.NewGuid(), FixedUtcNow);

        Assert.Equal(Document.PendingReviewReviewStatus, document.ReviewStatus);
        Assert.NotEqual(tokenBefore, document.ConcurrencyToken);
    }

    [Fact]
    public void SubmitForReview_without_a_file_throws()
    {
        var document = CreateDraftDocument();

        Assert.Throws<InvalidOperationException>(() => document.SubmitForReview(Guid.NewGuid(), FixedUtcNow));
    }

    [Fact]
    public void SubmitForReview_when_not_draft_throws()
    {
        var document = CreatePendingReviewDocument();

        Assert.Throws<InvalidOperationException>(() => document.SubmitForReview(Guid.NewGuid(), FixedUtcNow));
    }

    // ---- Approve ----

    [Fact]
    public void Approve_moves_pending_review_to_approved_and_changes_concurrency_token()
    {
        var document = CreatePendingReviewDocument();
        var tokenBefore = document.ConcurrencyToken;

        document.Approve(Guid.NewGuid(), FixedUtcNow);

        Assert.Equal(Document.ApprovedReviewStatus, document.ReviewStatus);
        Assert.NotEqual(tokenBefore, document.ConcurrencyToken);
    }

    [Fact]
    public void Approve_when_not_pending_review_throws()
    {
        var document = CreateDraftDocumentWithFile();

        Assert.Throws<InvalidOperationException>(() => document.Approve(Guid.NewGuid(), FixedUtcNow));
    }

    // ---- Reject ----

    [Fact]
    public void Reject_moves_pending_review_to_rejected_and_changes_concurrency_token()
    {
        var document = CreatePendingReviewDocument();
        var tokenBefore = document.ConcurrencyToken;

        document.Reject(Guid.NewGuid(), "Missing signature page.", FixedUtcNow);

        Assert.Equal(Document.RejectedReviewStatus, document.ReviewStatus);
        Assert.NotEqual(tokenBefore, document.ConcurrencyToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_requires_non_blank_notes(string? notes)
    {
        var document = CreatePendingReviewDocument();

        Assert.Throws<ArgumentException>(() => document.Reject(Guid.NewGuid(), notes!, FixedUtcNow));
    }

    [Fact]
    public void Reject_when_not_pending_review_throws()
    {
        var document = CreateDraftDocumentWithFile();

        Assert.Throws<InvalidOperationException>(() => document.Reject(Guid.NewGuid(), "Notes.", FixedUtcNow));
    }

    // ---- ReturnToDraftAfterCorrection ----

    [Fact]
    public void ReturnToDraftAfterCorrection_moves_rejected_to_draft_and_changes_concurrency_token()
    {
        var document = CreatePendingReviewDocument();
        document.Reject(Guid.NewGuid(), "Notes.", FixedUtcNow);
        var tokenBefore = document.ConcurrencyToken;

        document.ReturnToDraftAfterCorrection();

        Assert.Equal(Document.DraftReviewStatus, document.ReviewStatus);
        Assert.NotEqual(tokenBefore, document.ConcurrencyToken);
    }

    [Fact]
    public void ReturnToDraftAfterCorrection_when_not_rejected_throws()
    {
        var document = CreateDraftDocumentWithFile();

        Assert.Throws<InvalidOperationException>(() => document.ReturnToDraftAfterCorrection());
    }

    // ---- Revoke ----

    [Fact]
    public void Revoke_active_approved_document_sets_status_revoked_keeps_review_status_and_changes_concurrency_token()
    {
        var document = CreateApprovedActiveDocument();
        var tokenBefore = document.ConcurrencyToken;

        document.Revoke(Guid.NewGuid(), "No longer valid.", FixedUtcNow);

        Assert.Equal(Document.RevokedStatus, document.Status);
        Assert.Equal(Document.ApprovedReviewStatus, document.ReviewStatus);
        Assert.NotEqual(tokenBefore, document.ConcurrencyToken);
    }

    [Fact]
    public void Revoke_when_status_not_active_throws()
    {
        var document = CreateApprovedActiveDocument();
        document.Supersede(Guid.NewGuid(), FixedUtcNow.UtcDateTime);

        Assert.Throws<InvalidOperationException>(() => document.Revoke(Guid.NewGuid(), "Reason.", FixedUtcNow));
    }

    [Fact]
    public void Revoke_when_review_status_not_approved_throws()
    {
        var document = CreateDraftDocumentWithFile();

        Assert.Throws<InvalidOperationException>(() => document.Revoke(Guid.NewGuid(), "Reason.", FixedUtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Revoke_requires_non_blank_reason(string? reason)
    {
        var document = CreateApprovedActiveDocument();

        Assert.Throws<ArgumentException>(() => document.Revoke(Guid.NewGuid(), reason!, FixedUtcNow));
    }

    private static Document CreateDraftDocument() => new(
        Guid.NewGuid(),
        1,
        "CERT-1001",
        new DateOnly(2026, 1, 1),
        new DateOnly(2027, 1, 1),
        "Flag State Authority",
        Document.ActiveStatus);

    private static Document CreateDraftDocumentWithFile()
    {
        var document = CreateDraftDocument();
        document.AttachFile("file.pdf", "application/pdf", "storage-key", 100, Guid.NewGuid(), FixedUtcNow.UtcDateTime);

        return document;
    }

    private static Document CreatePendingReviewDocument()
    {
        var document = CreateDraftDocumentWithFile();
        document.SubmitForReview(Guid.NewGuid(), FixedUtcNow);

        return document;
    }

    private static Document CreateApprovedActiveDocument()
    {
        var document = CreatePendingReviewDocument();
        document.Approve(Guid.NewGuid(), FixedUtcNow);

        return document;
    }
}
