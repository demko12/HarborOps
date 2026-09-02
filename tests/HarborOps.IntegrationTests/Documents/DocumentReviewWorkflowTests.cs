using System.Net;
using System.Net.Http.Json;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace HarborOps.IntegrationTests.Documents;

using static DocumentReviewWorkflowTestSupport;

public sealed class DocumentReviewWorkflowTests : DocumentFileStorageIntegrationTestBase
{
    private readonly DocumentFileStorageApiFactory _application;

    public DocumentReviewWorkflowTests(DocumentFileStorageApiFactory application)
        : base(application)
    {
        _application = application;
    }

    // ---- submit-for-review ----

    [Fact]
    public async Task SubmitForReview_with_draft_document_and_file_returns_204()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreateDraftDocumentWithFileAsync(client, documentTypeId);

        using var response = await SubmitForReviewAsync(client, documentId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var detailsResponse = await client.GetAsync($"/api/documents/{documentId}", CancellationToken.None);
        var details = await detailsResponse.Content.ReadFromJsonAsync<DocumentDetailsResponse>(CancellationToken.None);
        Assert.Equal(Document.PendingReviewReviewStatus, details!.ReviewStatus);
    }

    [Fact]
    public async Task SubmitForReview_when_document_does_not_exist_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var response = await SubmitForReviewAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SubmitForReview_without_a_file_returns_409()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(client, documentTypeId);

        using var response = await SubmitForReviewAsync(client, documentId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SubmitForReview_when_already_pending_review_returns_409()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreatePendingReviewDocumentAsync(client, documentTypeId);

        using var response = await SubmitForReviewAsync(client, documentId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SubmitForReview_with_read_only_permission_returns_403()
    {
        using var manageClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreateDraftDocumentWithFileAsync(manageClient, documentTypeId);

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await SubmitForReviewAsync(readClient, documentId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- review ----

    [Fact]
    public async Task Review_approve_returns_204_and_sets_approved()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreatePendingReviewDocumentAsync(submitter, documentTypeId);

        using var response = await ReviewAsync(reviewer, documentId, Document.ApprovedReviewStatus);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var detailsResponse = await reviewer.GetAsync($"/api/documents/{documentId}", CancellationToken.None);
        var details = await detailsResponse.Content.ReadFromJsonAsync<DocumentDetailsResponse>(CancellationToken.None);
        Assert.Equal(Document.ApprovedReviewStatus, details!.ReviewStatus);
    }

    [Fact]
    public async Task Review_reject_returns_204_and_history_shows_notes()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreatePendingReviewDocumentAsync(submitter, documentTypeId);

        using var response = await ReviewAsync(reviewer, documentId, Document.RejectedReviewStatus, "Missing signature page.");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var detailsResponse = await reviewer.GetAsync($"/api/documents/{documentId}", CancellationToken.None);
        var details = await detailsResponse.Content.ReadFromJsonAsync<DocumentDetailsResponse>(CancellationToken.None);
        Assert.Equal(Document.RejectedReviewStatus, details!.ReviewStatus);

        using var historyResponse = await reviewer.GetAsync($"/api/documents/{documentId}/review-history", CancellationToken.None);
        var history = await historyResponse.Content.ReadFromJsonAsync<List<DocumentReviewEventResponse>>(CancellationToken.None);
        var rejectedEvent = Assert.Single(history!, item => item.Action == DocumentReviewEvent.RejectedAction);
        Assert.Equal("Missing signature page.", rejectedEvent.Notes);
    }

    [Fact]
    public async Task Review_reject_without_notes_returns_400()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreatePendingReviewDocumentAsync(submitter, documentTypeId);

        using var response = await ReviewAsync(reviewer, documentId, Document.RejectedReviewStatus, notes: null);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(ReviewDocumentValidator.NotesField, problem.Errors);
    }

    [Fact]
    public async Task Review_with_unknown_decision_returns_400()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreatePendingReviewDocumentAsync(submitter, documentTypeId);

        using var response = await ReviewAsync(reviewer, documentId, "Maybe");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(ReviewDocumentValidator.DecisionField, problem.Errors);
    }

    [Fact]
    public async Task Review_self_review_returns_409()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreatePendingReviewDocumentAsync(client, documentTypeId);

        using var response = await ReviewAsync(client, documentId, Document.ApprovedReviewStatus);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Review_when_not_pending_review_returns_409()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreateDraftDocumentWithFileAsync(client, documentTypeId);

        using var response = await ReviewAsync(client, documentId, Document.ApprovedReviewStatus);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Review_when_document_does_not_exist_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var response = await ReviewAsync(client, Guid.NewGuid(), Document.ApprovedReviewStatus);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Review_with_read_only_permission_returns_403()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreatePendingReviewDocumentAsync(submitter, documentTypeId);

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await ReviewAsync(readClient, documentId, Document.ApprovedReviewStatus);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- revoke ----

    [Fact]
    public async Task Revoke_active_approved_document_returns_204()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreateApprovedDocumentAsync(submitter, reviewer, documentTypeId);

        using var response = await RevokeAsync(reviewer, documentId, "No longer valid.");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var detailsResponse = await reviewer.GetAsync($"/api/documents/{documentId}", CancellationToken.None);
        var details = await detailsResponse.Content.ReadFromJsonAsync<DocumentDetailsResponse>(CancellationToken.None);
        Assert.Equal(Document.RevokedStatus, details!.Status);
        Assert.Equal(Document.ApprovedReviewStatus, details.ReviewStatus);
    }

    [Fact]
    public async Task Revoke_when_not_active_and_approved_returns_409()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreateDraftDocumentWithFileAsync(client, documentTypeId);

        using var response = await RevokeAsync(client, documentId, "No longer valid.");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_when_already_revoked_returns_409()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreateApprovedDocumentAsync(submitter, reviewer, documentTypeId);
        using (var firstRevoke = await RevokeAsync(reviewer, documentId, "No longer valid."))
        {
            Assert.Equal(HttpStatusCode.NoContent, firstRevoke.StatusCode);
        }

        using var response = await RevokeAsync(reviewer, documentId, "Again.");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_with_empty_reason_returns_400()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreateApprovedDocumentAsync(submitter, reviewer, documentTypeId);

        using var response = await RevokeAsync(reviewer, documentId, "   ");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RevokeDocumentValidator.ReasonField, problem.Errors);
    }

    [Fact]
    public async Task Revoke_when_document_does_not_exist_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var response = await RevokeAsync(client, Guid.NewGuid(), "No longer valid.");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_with_read_only_permission_returns_403()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreateApprovedDocumentAsync(submitter, reviewer, documentTypeId);

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await RevokeAsync(readClient, documentId, "No longer valid.");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- review-history ----

    [Fact]
    public async Task ReviewHistory_returns_events_in_deterministic_action_order()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);

        var documentId = await CreatePendingReviewDocumentAsync(submitter, documentTypeId);
        using (var reject = await ReviewAsync(reviewer, documentId, Document.RejectedReviewStatus, "Missing signature page."))
        {
            Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);
        }

        var correctionRequest = new CorrectDocumentMetadataRequest(
            documentTypeId, $"CERT-{Guid.NewGuid():N}", new DateOnly(2026, 2, 1), new DateOnly(2027, 2, 1), "Flag State Authority");
        using (var correction = await submitter.PutAsJsonAsync($"/api/documents/{documentId}/metadata", correctionRequest, CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.NoContent, correction.StatusCode);
        }

        using (var resubmit = await SubmitForReviewAsync(submitter, documentId))
        {
            Assert.Equal(HttpStatusCode.NoContent, resubmit.StatusCode);
        }

        using (var approve = await ReviewAsync(reviewer, documentId, Document.ApprovedReviewStatus))
        {
            Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);
        }

        using var historyResponse = await reviewer.GetAsync($"/api/documents/{documentId}/review-history", CancellationToken.None);
        var history = await historyResponse.Content.ReadFromJsonAsync<List<DocumentReviewEventResponse>>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.NotNull(history);

        // With TimeProvider fixed for determinism (see DocumentFileStorageApiFactory), all
        // four events created here share the exact same OccurredAtUtc, so
        // GetReviewHistoryAsync's OrderBy(OccurredAtUtc).ThenBy(Id) falls back entirely to
        // the Id tie-breaker - the returned order is therefore NOT the chronological action
        // sequence (Submitted, Rejected, Submitted, Approved), it is whichever order the
        // server-generated event Ids happen to sort into. Genuine chronological-order
        // behavior (distinct OccurredAtUtc values) is covered at the Application layer by
        // DocumentLifecycleServiceReviewTests.GetReviewHistoryAsync_orders_ties_by_id_when_occurred_at_utc_is_identical.
        // What this integration test CAN meaningfully assert without control over time: all
        // four expected events exist exactly once, and the returned order is consistent
        // with the documented Id tie-breaker.
        Assert.Equal(
            new[]
            {
                DocumentReviewEvent.SubmittedAction, DocumentReviewEvent.SubmittedAction,
                DocumentReviewEvent.RejectedAction, DocumentReviewEvent.ApprovedAction,
            }.Order(),
            history.Select(item => item.Action).Order());
        Assert.Equal(history.OrderBy(item => item.Id).Select(item => item.Id), history.Select(item => item.Id));
    }

    [Fact]
    public async Task ReviewHistory_returns_empty_list_for_a_document_with_no_events()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(client, documentTypeId);

        using var response = await client.GetAsync($"/api/documents/{documentId}/review-history", CancellationToken.None);
        var history = await response.Content.ReadFromJsonAsync<List<DocumentReviewEventResponse>>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(history!);
    }

    [Fact]
    public async Task ReviewHistory_when_document_does_not_exist_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var response = await client.GetAsync($"/api/documents/{Guid.NewGuid()}/review-history", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReviewHistory_manage_permission_always_visible()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreatePendingReviewDocumentAsync(submitter, documentTypeId);

        using var manageClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var response = await manageClient.GetAsync($"/api/documents/{documentId}/review-history", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReviewHistory_read_only_permission_returns_403_when_not_approved()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreatePendingReviewDocumentAsync(submitter, documentTypeId);

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await readClient.GetAsync($"/api/documents/{documentId}/review-history", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReviewHistory_read_only_permission_returns_200_when_approved_and_active()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await CreateApprovedDocumentAsync(submitter, reviewer, documentTypeId);

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await readClient.GetAsync($"/api/documents/{documentId}/review-history", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
