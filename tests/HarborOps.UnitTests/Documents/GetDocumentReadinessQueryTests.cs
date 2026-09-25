using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class GetDocumentReadinessQueryTests
{
    private static readonly Guid VesselId = Guid.NewGuid();
    private static readonly Guid OtherVesselId = Guid.NewGuid();
    private static readonly DateOnly Today = DocumentRequirementTestData.Today;

    [Fact]
    public async Task A_vessel_without_enabled_requirements_is_not_configured_rather_than_ready()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id, isEnabled: false);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id);

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        Assert.Equal(DocumentReadinessRules.NotConfiguredVesselStatus, result.VesselStatus);
        Assert.Equal(new DocumentReadinessSummary(0, 0, 0), result.Summary);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task A_configured_but_absent_type_is_reported_missing()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, OtherVesselId, documentType.Id);

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        var item = Assert.Single(result.Items);
        Assert.Equal(DocumentReadinessRules.NotReadyVesselStatus, result.VesselStatus);
        Assert.Equal(DocumentReadinessRules.UnsatisfiedStatus, item.Status);
        Assert.Equal([DocumentReadinessRules.Missing], item.Blockers);
        Assert.Null(item.SelectedDocumentId);
        Assert.Empty(item.Candidates);
        Assert.Equal(new DocumentReadinessSummary(1, 0, 1), result.Summary);
    }

    [Fact]
    public async Task A_qualifying_document_satisfies_the_requirement()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var document = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id);

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        var item = Assert.Single(result.Items);
        Assert.Equal(DocumentReadinessRules.ReadyVesselStatus, result.VesselStatus);
        Assert.Equal(DocumentReadinessRules.SatisfiedStatus, item.Status);
        Assert.Empty(item.Blockers);
        Assert.Equal(document.Id, item.SelectedDocumentId);
        Assert.True(Assert.Single(item.Candidates).Qualifies);
        Assert.Equal(new DocumentReadinessSummary(1, 1, 0), result.Summary);
    }

    [Fact]
    public async Task Expiry_equal_to_required_through_plus_buffer_is_valid()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id, minimumRemainingValidityDays: 30);
        var requiredThrough = Today.AddDays(60);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id, expiryDate: requiredThrough.AddDays(30));

        var result = await QueryAsync(
            dbContext,
            new GetDocumentReadinessRequest { VesselId = VesselId, RequiredThrough = requiredThrough });

        var item = Assert.Single(result.Items);
        Assert.Equal(DocumentReadinessRules.SatisfiedStatus, item.Status);
        Assert.Equal(requiredThrough.AddDays(30), item.RequiredValidUntil);
        Assert.Equal(1, result.Summary.SatisfiedCount);
    }

    [Fact]
    public async Task Expiry_one_day_short_of_the_buffer_is_insufficient_validity()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id, minimumRemainingValidityDays: 30);
        var requiredThrough = Today.AddDays(60);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id, expiryDate: requiredThrough.AddDays(29));

        var result = await QueryAsync(
            dbContext,
            new GetDocumentReadinessRequest { VesselId = VesselId, RequiredThrough = requiredThrough });

        var item = Assert.Single(result.Items);
        Assert.Equal(DocumentReadinessRules.UnsatisfiedStatus, item.Status);
        Assert.Equal([DocumentReadinessRules.InsufficientValidity], item.Blockers);
        Assert.Equal(0, result.Summary.SatisfiedCount);
    }

    [Fact]
    public async Task A_future_issue_date_is_not_yet_effective()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id, issueDate: Today.AddDays(1));

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        Assert.Equal([DocumentReadinessRules.NotYetEffective], Assert.Single(result.Items).Blockers);
        Assert.Equal(0, result.Summary.SatisfiedCount);
    }

    [Fact]
    public async Task A_draft_without_file_reports_not_approved_and_missing_file()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id, approved: false, withFile: false);

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        Assert.Equal(
            [DocumentReadinessRules.NotApproved, DocumentReadinessRules.MissingFile],
            Assert.Single(result.Items).Blockers);
    }

    [Fact]
    public async Task Approval_changes_are_reflected_by_the_next_query()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var document = await DocumentRequirementTestData.SeedDocumentAsync(
            dbContext, VesselId, documentType.Id, approved: false, withFile: true);

        var before = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        document.SubmitForReview(Guid.NewGuid(), DocumentRequirementTestData.FixedUtcNow);
        document.Approve(Guid.NewGuid(), DocumentRequirementTestData.FixedUtcNow);
        await dbContext.SaveChangesAsync();

        var after = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        Assert.Equal([DocumentReadinessRules.NotApproved], Assert.Single(before.Items).Blockers);
        Assert.Equal(DocumentReadinessRules.SatisfiedStatus, Assert.Single(after.Items).Status);
    }

    [Fact]
    public async Task A_revoked_document_is_inactive()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var document = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id);
        document.Revoke(Guid.NewGuid(), "Issued in error.", DocumentRequirementTestData.FixedUtcNow);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        Assert.Equal([DocumentReadinessRules.InactiveOrRetained], Assert.Single(result.Items).Blockers);
    }

    [Theory]
    [InlineData(DocumentRetentionState.ArchivedState)]
    [InlineData(DocumentRetentionState.PendingDispositionState)]
    [InlineData(DocumentRetentionState.DisposedState)]
    public async Task A_document_outside_active_retention_is_inactive_or_retained(string retentionState)
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var document = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id);
        await DocumentRequirementTestData.SetRetentionStateAsync(dbContext, document.Id, retentionState);

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        Assert.Equal([DocumentReadinessRules.InactiveOrRetained], Assert.Single(result.Items).Blockers);
        Assert.Equal(0, result.Summary.SatisfiedCount);
    }

    [Fact]
    public async Task Restoring_an_archived_document_is_reflected_by_the_next_query()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var document = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id);
        await DocumentRequirementTestData.SetRetentionStateAsync(dbContext, document.Id, DocumentRetentionState.ArchivedState);

        var before = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        var state = await dbContext.Set<DocumentRetentionState>().SingleAsync(candidate => candidate.DocumentId == document.Id);
        state.Restore(Guid.NewGuid(), DocumentRequirementTestData.FixedUtcNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        var after = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        Assert.Equal(DocumentReadinessRules.UnsatisfiedStatus, Assert.Single(before.Items).Status);
        Assert.Equal(DocumentReadinessRules.SatisfiedStatus, Assert.Single(after.Items).Status);
    }

    [Fact]
    public async Task Renewal_never_satisfies_the_type_with_the_superseded_predecessor()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var predecessor = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id, number: "CERT-OLD");

        var successor = new Document(
            VesselId, documentType.Id, "CERT-NEW", Today, Today.AddYears(3), "Flag State Authority", Document.ActiveStatus);
        successor.LinkToPredecessor(predecessor.Id);
        predecessor.Supersede(Guid.NewGuid(), DocumentRequirementTestData.FixedUtcNow.UtcDateTime);
        await dbContext.Set<Document>().AddAsync(successor);
        await dbContext.SaveChangesAsync();
        var successorId = successor.Id;

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        var item = Assert.Single(result.Items);
        Assert.Equal(DocumentReadinessRules.UnsatisfiedStatus, item.Status);
        Assert.Equal([DocumentReadinessRules.NotApproved, DocumentReadinessRules.MissingFile], item.Blockers);
        Assert.Equal(successorId, Assert.Single(item.Candidates).DocumentId);
        Assert.Null(item.SelectedDocumentId);
    }

    [Fact]
    public async Task The_display_candidate_is_the_latest_expiry_then_issue_date_then_id()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var expiry = Today.AddYears(2);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id, expiryDate: expiry.AddDays(-1));
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id, issueDate: Today.AddDays(-10), expiryDate: expiry);
        var first = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id, issueDate: Today.AddDays(-5), expiryDate: expiry);
        var second = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id, issueDate: Today.AddDays(-5), expiryDate: expiry);
        var expected = first.Id.CompareTo(second.Id) < 0 ? first.Id : second.Id;

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        var item = Assert.Single(result.Items);
        Assert.Equal(expected, item.SelectedDocumentId);
        Assert.Equal(expected, item.Candidates[0].DocumentId);
        Assert.Equal(4, item.Candidates.Count);
        Assert.All(item.Candidates, candidate => Assert.True(candidate.Qualifies));
        Assert.Equal(new DocumentReadinessSummary(1, 1, 0), result.Summary);
    }

    [Fact]
    public async Task Blockers_from_several_documents_are_combined_and_the_requirement_is_counted_once()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id, expiryDate: Today.AddDays(-1));
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id, approved: false);

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        var item = Assert.Single(result.Items);
        Assert.Equal([DocumentReadinessRules.NotApproved, DocumentReadinessRules.InsufficientValidity], item.Blockers);
        Assert.Equal(new DocumentReadinessSummary(1, 0, 1), result.Summary);
    }

    [Fact]
    public async Task Read_only_callers_do_not_see_unapproved_candidates_but_keep_the_blockers()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id, approved: false);

        var readOnly = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId }, restrictToReadOnlyVisible: true);
        var manager = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId }, restrictToReadOnlyVisible: false);

        Assert.Empty(Assert.Single(readOnly.Items).Candidates);
        Assert.Equal([DocumentReadinessRules.NotApproved], readOnly.Items[0].Blockers);
        Assert.Single(Assert.Single(manager.Items).Candidates);
    }

    [Fact]
    public async Task Status_filter_and_paging_apply_after_the_summary_is_counted()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var satisfiedType = await DocumentRequirementTestData.SeedTypeAsync(dbContext, "A Certificate");
        var missingType = await DocumentRequirementTestData.SeedTypeAsync(dbContext, "B Certificate");
        var otherMissingType = await DocumentRequirementTestData.SeedTypeAsync(dbContext, "C Certificate");
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, satisfiedType.Id);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, missingType.Id);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, otherMissingType.Id);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, satisfiedType.Id);

        var result = await QueryAsync(
            dbContext,
            new GetDocumentReadinessRequest
            {
                VesselId = VesselId,
                Status = DocumentReadinessRules.UnsatisfiedStatus,
                PageNumber = 2,
                PageSize = 1,
            });

        Assert.Equal(new DocumentReadinessSummary(3, 1, 2), result.Summary);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal("C Certificate", Assert.Single(result.Items).DocumentTypeName);
    }

    [Fact]
    public async Task Without_a_vessel_the_matrix_spans_every_vessel_with_enabled_requirements()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, OtherVesselId, documentType.Id);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, OtherVesselId, documentType.Id);

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest());

        Assert.Null(result.VesselStatus);
        Assert.Equal(new DocumentReadinessSummary(2, 1, 1), result.Summary);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task Querying_does_not_create_documents_or_requirements()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);

        await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        Assert.Empty(dbContext.Set<Document>());
        Assert.Single(dbContext.Set<VesselDocumentRequirement>());
    }

    [Fact]
    public async Task An_unknown_vessel_is_rejected()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();

        await Assert.ThrowsAsync<DocumentRequirementVesselNotFoundException>(
            () => QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = Guid.NewGuid() }));
    }

    [Theory]
    [InlineData(-1, null, null, GetDocumentReadinessQuery.RequiredThroughField)]
    [InlineData(366, null, null, GetDocumentReadinessQuery.RequiredThroughField)]
    [InlineData(0, "Ready", null, GetDocumentReadinessQuery.StatusField)]
    [InlineData(0, null, 101, GetDocumentReadinessQuery.PageSizeField)]
    public async Task Invalid_filters_are_rejected(int requiredThroughOffset, string? status, int? pageSize, string field)
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetDocumentReadinessValidationException>(() => QueryAsync(
            dbContext,
            new GetDocumentReadinessRequest
            {
                RequiredThrough = Today.AddDays(requiredThroughOffset),
                Status = status,
                PageSize = pageSize,
            }));

        Assert.Contains(field, exception.Errors.Keys);
    }

    [Fact]
    public async Task Required_through_accepts_the_365_day_horizon()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { RequiredThrough = Today.AddDays(365) });

        Assert.Equal(Today.AddDays(365), result.RequiredThrough);
        Assert.Equal(Today, result.Today);
    }

    [Fact]
    public async Task A_selected_document_without_an_issue_is_not_issued()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id);

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        var distribution = Assert.Single(result.Items).Distribution;
        Assert.NotNull(distribution);
        Assert.Equal(DocumentReadinessRules.NotIssuedDistributionStatus, distribution!.Status);
        Assert.Null(distribution.IssueId);
        Assert.Equal(0, distribution.PendingCount + distribution.AcknowledgedCount + distribution.OverdueCount);
    }

    [Fact]
    public async Task The_current_issue_reports_acknowledged_pending_and_overdue_recipients()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var document = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id);
        var acknowledged = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var issuedAt = DocumentRequirementTestData.FixedUtcNow.UtcDateTime.AddDays(-3);
        var onTime = await DocumentDistributionTestData.SeedIssueAsync(
            dbContext, document, Today.AddDays(5), issuedAt,
            (acknowledged, DocumentDistributionRecipient.AcknowledgedState),
            (pending, DocumentDistributionRecipient.PendingState));
        var overdueUser = Guid.NewGuid();
        var overdueIssue = await DocumentDistributionTestData.SeedIssueAsync(
            dbContext, document, Today.AddDays(-1), issuedAt.AddHours(1),
            (acknowledged, DocumentDistributionRecipient.AcknowledgedState),
            (pending, DocumentDistributionRecipient.PendingState),
            (overdueUser, DocumentDistributionRecipient.PendingState));
        var emails = new Dictionary<Guid, string> { [overdueUser] = "chief.engineer@harborops.test" };

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId }, emails: emails);

        var distribution = Assert.Single(result.Items).Distribution!;
        Assert.Equal(DocumentReadinessRules.IssuedDistributionStatus, distribution.Status);
        Assert.Equal(overdueIssue.Id, distribution.IssueId);
        Assert.NotEqual(onTime.Id, distribution.IssueId);
        Assert.Equal(1, distribution.AcknowledgedCount);
        Assert.Equal(0, distribution.PendingCount);
        Assert.Equal(2, distribution.OverdueCount);
        Assert.Equal(3, distribution.Recipients!.Count);
        Assert.Contains(distribution.Recipients, recipient => recipient.Email == "chief.engineer@harborops.test" && recipient.IsOverdue);
    }

    [Fact]
    public async Task Read_only_callers_get_aggregate_distribution_counts_without_recipients()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var document = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id);
        await DocumentDistributionTestData.SeedIssueAsync(
            dbContext, document, Today.AddDays(5), DocumentRequirementTestData.FixedUtcNow.UtcDateTime,
            (Guid.NewGuid(), DocumentDistributionRecipient.PendingState));

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId }, restrictToReadOnlyVisible: true);

        var distribution = Assert.Single(result.Items).Distribution!;
        Assert.Equal(1, distribution.PendingCount);
        Assert.Null(distribution.Recipients);
    }

    [Fact]
    public async Task Acknowledgements_of_an_older_file_revision_are_not_reused()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var document = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id);
        await DocumentDistributionTestData.SeedIssueAsync(
            dbContext, document, Today.AddDays(5), DocumentRequirementTestData.FixedUtcNow.UtcDateTime,
            (Guid.NewGuid(), DocumentDistributionRecipient.AcknowledgedState));
        document.AttachFile("certificate-v2.pdf", "application/pdf", $"key-{Guid.NewGuid():N}", 2048, Guid.NewGuid(), DocumentRequirementTestData.FixedUtcNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        var item = Assert.Single(result.Items);
        Assert.Equal(DocumentReadinessRules.SatisfiedStatus, item.Status);
        Assert.Equal(DocumentReadinessRules.NotIssuedDistributionStatus, item.Distribution!.Status);
        Assert.Equal(0, item.Distribution.AcknowledgedCount);
    }

    [Fact]
    public async Task A_cancelled_issue_is_skipped_for_the_most_recent_active_one()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var document = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id);
        var issuedAt = DocumentRequirementTestData.FixedUtcNow.UtcDateTime.AddDays(-2);
        var active = await DocumentDistributionTestData.SeedIssueAsync(
            dbContext, document, Today.AddDays(5), issuedAt, (Guid.NewGuid(), DocumentDistributionRecipient.AcknowledgedState));
        var cancelled = await DocumentDistributionTestData.SeedIssueAsync(
            dbContext, document, Today.AddDays(5), issuedAt.AddDays(1), (Guid.NewGuid(), DocumentDistributionRecipient.PendingState));
        cancelled.Cancel(Guid.NewGuid(), "Wrong recipients.", issuedAt.AddDays(1).AddHours(1));
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        Assert.Equal(active.Id, Assert.Single(result.Items).Distribution!.IssueId);
    }

    [Fact]
    public async Task Completed_distribution_never_makes_an_unusable_document_satisfy_the_requirement()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var document = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id);
        await DocumentDistributionTestData.SeedIssueAsync(
            dbContext, document, Today.AddDays(5), DocumentRequirementTestData.FixedUtcNow.UtcDateTime,
            (Guid.NewGuid(), DocumentDistributionRecipient.AcknowledgedState));
        document.Revoke(Guid.NewGuid(), "Issued in error.", DocumentRequirementTestData.FixedUtcNow);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        var item = Assert.Single(result.Items);
        Assert.Equal(DocumentReadinessRules.UnsatisfiedStatus, item.Status);
        Assert.Null(item.SelectedDocumentId);
        Assert.Null(item.Distribution);
    }

    [Fact]
    public async Task Attaching_a_file_is_reflected_by_the_next_query()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var document = await DocumentRequirementTestData.SeedDocumentAsync(
            dbContext, VesselId, documentType.Id, approved: false, withFile: false);

        var before = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        document.AttachFile("certificate.pdf", "application/pdf", $"key-{Guid.NewGuid():N}", 1024, Guid.NewGuid(), DocumentRequirementTestData.FixedUtcNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        var afterUpload = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        document.SubmitForReview(Guid.NewGuid(), DocumentRequirementTestData.FixedUtcNow);
        document.Approve(Guid.NewGuid(), DocumentRequirementTestData.FixedUtcNow);
        await dbContext.SaveChangesAsync();

        var afterApproval = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        Assert.Equal([DocumentReadinessRules.NotApproved, DocumentReadinessRules.MissingFile], Assert.Single(before.Items).Blockers);
        Assert.Equal([DocumentReadinessRules.NotApproved], Assert.Single(afterUpload.Items).Blockers);
        Assert.Equal(DocumentReadinessRules.SatisfiedStatus, Assert.Single(afterApproval.Items).Status);
    }

    [Fact]
    public async Task Managers_see_each_recipient_state_change_with_its_reason()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var document = await DocumentRequirementTestData.SeedDocumentAsync(dbContext, VesselId, documentType.Id);
        var acknowledged = Guid.NewGuid();
        var obsolete = Guid.NewGuid();
        var issuedAt = DocumentRequirementTestData.FixedUtcNow.UtcDateTime.AddDays(-2);
        await DocumentDistributionTestData.SeedIssueAsync(
            dbContext, document, Today.AddDays(5), issuedAt,
            (acknowledged, DocumentDistributionRecipient.AcknowledgedState),
            (obsolete, DocumentDistributionRecipient.ObsoleteState));

        var result = await QueryAsync(dbContext, new GetDocumentReadinessRequest { VesselId = VesselId });

        var recipients = Assert.Single(result.Items).Distribution!.Recipients!;
        var acknowledgedRecipient = Assert.Single(recipients, recipient => recipient.UserId == acknowledged);
        Assert.Equal(issuedAt.AddHours(1), acknowledgedRecipient.AcknowledgedAtUtc);
        Assert.Equal(issuedAt.AddHours(1), acknowledgedRecipient.StateChangedAtUtc);
        Assert.Null(acknowledgedRecipient.StateReason);
        var obsoleteRecipient = Assert.Single(recipients, recipient => recipient.UserId == obsolete);
        Assert.Equal(DocumentDistributionRecipient.ObsoleteState, obsoleteRecipient.State);
        Assert.Equal(issuedAt.AddHours(1), obsoleteRecipient.StateChangedAtUtc);
        Assert.Equal("Replaced.", obsoleteRecipient.StateReason);
    }

    private static Task<GetDocumentReadinessResponse> QueryAsync(
        HarborOpsDbContext dbContext,
        GetDocumentReadinessRequest request,
        bool restrictToReadOnlyVisible = false,
        IReadOnlyDictionary<Guid, string>? emails = null) =>
        new GetDocumentReadinessQuery(
                dbContext,
                new FakeDocumentsVesselLookup(VesselId, OtherVesselId),
                new FakeDistributionRecipientLookup(emails),
                new FixedTimeProvider(DocumentRequirementTestData.FixedUtcNow))
            .GetAsync(request, restrictToReadOnlyVisible, CancellationToken.None);
}
