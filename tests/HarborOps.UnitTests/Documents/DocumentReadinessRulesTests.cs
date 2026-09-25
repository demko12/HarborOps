using HarborOps.Modules.Documents.Domain;

namespace HarborOps.UnitTests.Documents;

public sealed class DocumentReadinessRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 24);

    [Fact]
    public void EvaluateDocument_returns_no_blockers_for_an_active_approved_filed_document()
    {
        var blockers = DocumentReadinessRules.EvaluateDocument(Candidate(), Today, Today.AddDays(30));

        Assert.Empty(blockers);
    }

    [Fact]
    public void EvaluateDocument_accepts_expiry_equal_to_the_validity_threshold()
    {
        var threshold = Today.AddDays(30);

        var blockers = DocumentReadinessRules.EvaluateDocument(Candidate(expiryDate: threshold), Today, threshold);

        Assert.Empty(blockers);
    }

    [Fact]
    public void EvaluateDocument_reports_insufficient_validity_one_day_before_the_threshold()
    {
        var threshold = Today.AddDays(30);

        var blockers = DocumentReadinessRules.EvaluateDocument(
            Candidate(expiryDate: threshold.AddDays(-1)), Today, threshold);

        Assert.Equal([DocumentReadinessRules.InsufficientValidity], blockers);
    }

    [Fact]
    public void EvaluateDocument_reports_a_future_issue_date_as_not_yet_effective()
    {
        var blockers = DocumentReadinessRules.EvaluateDocument(
            Candidate(issueDate: Today.AddDays(1)), Today, Today);

        Assert.Equal([DocumentReadinessRules.NotYetEffective], blockers);
    }

    [Fact]
    public void EvaluateDocument_accepts_an_issue_date_of_today()
    {
        var blockers = DocumentReadinessRules.EvaluateDocument(Candidate(issueDate: Today), Today, Today);

        Assert.Empty(blockers);
    }

    [Fact]
    public void EvaluateDocument_reports_every_applicable_blocker()
    {
        var candidate = Candidate(
            status: Document.RevokedStatus,
            reviewStatus: Document.DraftReviewStatus,
            hasFile: false,
            issueDate: Today.AddDays(2),
            expiryDate: Today.AddDays(5));

        var blockers = DocumentReadinessRules.EvaluateDocument(candidate, Today, Today.AddDays(10));

        Assert.Equal(
            [
                DocumentReadinessRules.InactiveOrRetained,
                DocumentReadinessRules.NotApproved,
                DocumentReadinessRules.MissingFile,
                DocumentReadinessRules.NotYetEffective,
                DocumentReadinessRules.InsufficientValidity,
            ],
            blockers);
    }

    [Theory]
    [InlineData(DocumentRetentionState.ArchivedState)]
    [InlineData(DocumentRetentionState.PendingDispositionState)]
    [InlineData(DocumentRetentionState.DisposedState)]
    public void EvaluateDocument_reports_a_document_outside_active_retention(string retentionState)
    {
        var blockers = DocumentReadinessRules.EvaluateDocument(
            Candidate(retentionState: retentionState), Today, Today);

        Assert.Equal([DocumentReadinessRules.InactiveOrRetained], blockers);
    }

    [Fact]
    public void EvaluatedCandidates_ignores_superseded_predecessors_when_a_current_document_exists()
    {
        var predecessor = Candidate(status: Document.SupersededStatus);
        var successor = Candidate(reviewStatus: Document.DraftReviewStatus);

        var evaluated = DocumentReadinessRules.EvaluatedCandidates([predecessor, successor]);

        Assert.Equal([successor], evaluated);
    }

    [Fact]
    public void CombineBlockers_lists_each_blocker_once_in_a_stable_order()
    {
        var combined = DocumentReadinessRules.CombineBlockers(
            [
                [DocumentReadinessRules.InsufficientValidity, DocumentReadinessRules.NotApproved],
                [DocumentReadinessRules.NotApproved, DocumentReadinessRules.InactiveOrRetained],
            ]);

        Assert.Equal(
            [
                DocumentReadinessRules.InactiveOrRetained,
                DocumentReadinessRules.NotApproved,
                DocumentReadinessRules.InsufficientValidity,
            ],
            combined);
    }

    private static DocumentReadinessCandidate Candidate(
        string status = Document.ActiveStatus,
        string reviewStatus = Document.ApprovedReviewStatus,
        bool hasFile = true,
        string retentionState = DocumentRetentionState.ActiveState,
        DateOnly? issueDate = null,
        DateOnly? expiryDate = null) =>
        new(
            Guid.NewGuid(),
            "CERT-1",
            status,
            reviewStatus,
            hasFile,
            retentionState,
            issueDate ?? Today.AddYears(-1),
            expiryDate ?? Today.AddYears(1));
}
