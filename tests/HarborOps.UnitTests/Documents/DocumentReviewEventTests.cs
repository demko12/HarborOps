using HarborOps.Modules.Documents.Domain;

namespace HarborOps.UnitTests.Documents;

public sealed class DocumentReviewEventTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_rejects_unknown_action()
    {
        Assert.Throws<ArgumentException>(() =>
            new DocumentReviewEvent(Guid.NewGuid(), "Unknown", Guid.NewGuid(), FixedUtcNow, null));
    }

    [Theory]
    [InlineData(DocumentReviewEvent.RejectedAction)]
    [InlineData(DocumentReviewEvent.RevokedAction)]
    public void Constructor_rejects_blank_notes_for_rejected_and_revoked(string action)
    {
        Assert.Throws<ArgumentException>(() =>
            new DocumentReviewEvent(Guid.NewGuid(), action, Guid.NewGuid(), FixedUtcNow, "   "));
    }

    [Theory]
    [InlineData(DocumentReviewEvent.SubmittedAction)]
    [InlineData(DocumentReviewEvent.ApprovedAction)]
    public void Constructor_allows_null_notes_for_submitted_and_approved(string action)
    {
        var reviewEvent = new DocumentReviewEvent(Guid.NewGuid(), action, Guid.NewGuid(), FixedUtcNow, null);

        Assert.Null(reviewEvent.Notes);
    }

    [Fact]
    public void Constructor_sets_all_fields()
    {
        var documentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var reviewEvent = new DocumentReviewEvent(documentId, DocumentReviewEvent.ApprovedAction, actorId, FixedUtcNow, "All good.");

        Assert.NotEqual(Guid.Empty, reviewEvent.Id);
        Assert.Equal(documentId, reviewEvent.DocumentId);
        Assert.Equal(DocumentReviewEvent.ApprovedAction, reviewEvent.Action);
        Assert.Equal(actorId, reviewEvent.ActorUserId);
        Assert.Equal(FixedUtcNow, reviewEvent.OccurredAtUtc);
        Assert.Equal("All good.", reviewEvent.Notes);
    }
}
