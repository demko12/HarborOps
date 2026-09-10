using System.Net;
using System.Net.Http.Json;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace HarborOps.IntegrationTests.Documents;

using static DocumentReviewWorkflowTestSupport;
using static RetentionWorkflowTestSupport;

public sealed class RetentionWorkflowTests : DocumentFileStorageIntegrationTestBase
{
    private readonly DocumentFileStorageApiFactory _application;

    public RetentionWorkflowTests(DocumentFileStorageApiFactory application)
        : base(application)
    {
        _application = application;
    }

    // Well before HarborOpsApiFactory.FixedUtcNow (2026-08-10), so a document expiring on
    // this date is always already Active-and-expired under the fixed test clock.
    private static readonly DateOnly LongExpiredDate = new(2026, 1, 1);

    // ---- retention policies ----

    [Fact]
    public async Task GetRetentionPolicies_returns_200_and_empty_list_when_none_configured()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await client.GetAsync("/api/documents/retention-policies", CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetRetentionPoliciesResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(result!.Items);
    }

    [Fact]
    public async Task GetRetentionPolicies_without_read_permission_returns_403()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Reports.Read);

        using var response = await client.GetAsync("/api/documents/retention-policies", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRetentionPolicy_creates_a_new_policy_and_returns_204()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage, Permissions.Documents.Read);
        var documentTypeId = await SeedDocumentTypeAsync(_application);

        using var response = await UpdateRetentionPolicyAsync(client, documentTypeId, 365);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var listResponse = await client.GetAsync("/api/documents/retention-policies", CancellationToken.None);
        var list = await listResponse.Content.ReadFromJsonAsync<GetRetentionPoliciesResponse>(CancellationToken.None);
        var item = Assert.Single(list!.Items);
        Assert.Equal(documentTypeId, item.DocumentTypeId);
        Assert.Equal(365, item.RetentionPeriodDays);
    }

    [Fact]
    public async Task UpdateRetentionPolicy_updates_an_existing_policy_and_returns_204()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage, Permissions.Documents.Read);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(client, documentTypeId, 365);

        using var response = await UpdateRetentionPolicyAsync(client, documentTypeId, 730);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var listResponse = await client.GetAsync("/api/documents/retention-policies", CancellationToken.None);
        var list = await listResponse.Content.ReadFromJsonAsync<GetRetentionPoliciesResponse>(CancellationToken.None);
        Assert.Equal(730, Assert.Single(list!.Items).RetentionPeriodDays);
    }

    [Fact]
    public async Task UpdateRetentionPolicy_with_non_positive_days_returns_400()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);

        using var response = await UpdateRetentionPolicyAsync(client, documentTypeId, 0);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRetentionPolicy_for_unknown_document_type_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var response = await UpdateRetentionPolicyAsync(client, 999_999, 365);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRetentionPolicy_with_read_only_permission_returns_403()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);
        var documentTypeId = await SeedDocumentTypeAsync(_application);

        using var response = await UpdateRetentionPolicyAsync(client, documentTypeId, 365);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- retention queue ----

    [Fact]
    public async Task GetRetentionQueue_returns_200_with_eligible_documents()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage, Permissions.Documents.Read);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(client, documentTypeId, 30);
        var documentId = await RegisterExpiredActiveDocumentAsync(client, documentTypeId, LongExpiredDate);

        using var response = await client.GetAsync("/api/documents/retention-queue", CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetRetentionQueueResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(result!.Items, item => item.DocumentId == documentId);
    }

    [Fact]
    public async Task GetRetentionQueue_without_read_permission_returns_403()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Reports.Read);

        using var response = await client.GetAsync("/api/documents/retention-queue", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- archive / restore ----

    [Fact]
    public async Task Archive_active_document_returns_200_and_sets_archived()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(client, documentTypeId);

        using var response = await ArchiveAsync(client, documentId);
        var result = await response.Content.ReadFromJsonAsync<DocumentRetentionStateResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Archived", result!.State);
    }

    [Fact]
    public async Task Archive_unknown_document_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var response = await ArchiveAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archive_when_already_archived_returns_409()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(client, documentTypeId);
        using (var first = await ArchiveAsync(client, documentId))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var response = await ArchiveAsync(client, documentId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Archive_with_read_only_permission_returns_403()
    {
        using var manageClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(manageClient, documentTypeId);

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await ArchiveAsync(readClient, documentId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Restore_archived_document_returns_200_and_sets_active()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(client, documentTypeId);
        using (var archive = await ArchiveAsync(client, documentId))
        {
            Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        }

        using var response = await RestoreAsync(client, documentId);
        var result = await response.Content.ReadFromJsonAsync<DocumentRetentionStateResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Active", result!.State);
    }

    [Fact]
    public async Task Restore_when_not_archived_returns_409()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(client, documentTypeId);

        using var response = await RestoreAsync(client, documentId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- legal holds ----

    [Fact]
    public async Task PlaceLegalHold_returns_201()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(client, documentTypeId);

        using var response = await PlaceLegalHoldAsync(client, documentId, "Litigation pending.", "CASE-1");
        var result = await response.Content.ReadFromJsonAsync<PlaceLegalHoldResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEqual(Guid.Empty, result!.Id);
    }

    [Fact]
    public async Task PlaceLegalHold_with_blank_reason_returns_400()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(client, documentTypeId);

        using var response = await PlaceLegalHoldAsync(client, documentId, reason: "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PlaceLegalHold_unknown_document_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var response = await PlaceLegalHoldAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PlaceLegalHold_with_read_only_permission_returns_403()
    {
        using var manageClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(manageClient, documentTypeId);

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await PlaceLegalHoldAsync(readClient, documentId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseLegalHold_returns_200()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(client, documentTypeId);
        var holdId = await PlaceActiveLegalHoldAsync(client, documentId);

        using var response = await ReleaseLegalHoldAsync(client, documentId, holdId);
        var result = await response.Content.ReadFromJsonAsync<ReleaseLegalHoldResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result!.IsActive);
    }

    [Fact]
    public async Task ReleaseLegalHold_unknown_hold_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(client, documentTypeId);

        using var response = await ReleaseLegalHoldAsync(client, documentId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseLegalHold_already_released_returns_409()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(client, documentTypeId);
        var holdId = await PlaceActiveLegalHoldAsync(client, documentId);
        using (var first = await ReleaseLegalHoldAsync(client, documentId, holdId))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var response = await ReleaseLegalHoldAsync(client, documentId, holdId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseLegalHold_with_blank_reason_returns_400()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await RegisterDocumentAsync(client, documentTypeId);
        var holdId = await PlaceActiveLegalHoldAsync(client, documentId);

        using var response = await ReleaseLegalHoldAsync(client, documentId, holdId, reason: "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- disposition requests ----

    [Fact]
    public async Task CreateDispositionRequest_returns_201_when_due()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(client, documentTypeId, 30);
        var documentId = await RegisterExpiredActiveDocumentAsync(client, documentTypeId, LongExpiredDate);

        using var response = await CreateDispositionRequestAsync(client, documentId);
        var result = await response.Content.ReadFromJsonAsync<DispositionRequestResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Pending", result!.Status);
    }

    [Fact]
    public async Task CreateDispositionRequest_when_not_yet_due_returns_409()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(client, documentTypeId, 50_000);
        var documentId = await RegisterExpiredActiveDocumentAsync(client, documentTypeId, LongExpiredDate);

        using var response = await CreateDispositionRequestAsync(client, documentId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateDispositionRequest_with_an_active_legal_hold_returns_409()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(client, documentTypeId, 30);
        var documentId = await RegisterExpiredActiveDocumentAsync(client, documentTypeId, LongExpiredDate);
        await PlaceActiveLegalHoldAsync(client, documentId);

        using var response = await CreateDispositionRequestAsync(client, documentId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateDispositionRequest_unknown_document_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var response = await CreateDispositionRequestAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateDispositionRequest_with_read_only_permission_returns_403()
    {
        using var manageClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(manageClient, documentTypeId, 30);
        var documentId = await RegisterExpiredActiveDocumentAsync(manageClient, documentTypeId, LongExpiredDate);

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await CreateDispositionRequestAsync(readClient, documentId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApproveDispositionRequest_by_a_different_user_returns_200()
    {
        using var requester = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var approver = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Dispose);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(requester, documentTypeId, 30);
        var documentId = await RegisterExpiredActiveDocumentAsync(requester, documentTypeId, LongExpiredDate);
        var requestId = await CreatePendingDispositionRequestAsync(requester, documentId);

        using var response = await ApproveDispositionRequestAsync(approver, documentId, requestId);
        var result = await response.Content.ReadFromJsonAsync<DispositionRequestResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Approved", result!.Status);
    }

    [Fact]
    public async Task ApproveDispositionRequest_by_the_requester_returns_409()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage, Permissions.Documents.Dispose);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(client, documentTypeId, 30);
        var documentId = await RegisterExpiredActiveDocumentAsync(client, documentTypeId, LongExpiredDate);
        var requestId = await CreatePendingDispositionRequestAsync(client, documentId);

        using var response = await ApproveDispositionRequestAsync(client, documentId, requestId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ApproveDispositionRequest_when_already_decided_returns_409()
    {
        using var requester = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var approver = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Dispose);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(requester, documentTypeId, 30);
        var documentId = await RegisterExpiredActiveDocumentAsync(requester, documentTypeId, LongExpiredDate);
        var requestId = await CreatePendingDispositionRequestAsync(requester, documentId);
        using (var first = await ApproveDispositionRequestAsync(approver, documentId, requestId))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var response = await ApproveDispositionRequestAsync(approver, documentId, requestId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ApproveDispositionRequest_unknown_request_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Dispose);

        using var response = await ApproveDispositionRequestAsync(client, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApproveDispositionRequest_with_manage_only_permission_returns_403()
    {
        using var requester = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(requester, documentTypeId, 30);
        var documentId = await RegisterExpiredActiveDocumentAsync(requester, documentTypeId, LongExpiredDate);
        var requestId = await CreatePendingDispositionRequestAsync(requester, documentId);

        using var manageOnlyClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var response = await ApproveDispositionRequestAsync(manageOnlyClient, documentId, requestId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectDispositionRequest_by_a_different_user_returns_200_and_reverts_state()
    {
        using var requester = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage, Permissions.Documents.Read);
        using var approver = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Dispose);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(requester, documentTypeId, 30);
        var documentId = await RegisterExpiredActiveDocumentAsync(requester, documentTypeId, LongExpiredDate);
        var requestId = await CreatePendingDispositionRequestAsync(requester, documentId);

        using var response = await RejectDispositionRequestAsync(approver, documentId, requestId);
        var result = await response.Content.ReadFromJsonAsync<DispositionRequestResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Rejected", result!.Status);

        using var queueResponse = await requester.GetAsync("/api/documents/retention-queue?state=Active", CancellationToken.None);
        var queue = await queueResponse.Content.ReadFromJsonAsync<GetRetentionQueueResponse>(CancellationToken.None);
        Assert.Contains(queue!.Items, item => item.DocumentId == documentId);
    }

    [Fact]
    public async Task RejectDispositionRequest_with_blank_decision_reason_returns_400()
    {
        using var requester = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var approver = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Dispose);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(requester, documentTypeId, 30);
        var documentId = await RegisterExpiredActiveDocumentAsync(requester, documentTypeId, LongExpiredDate);
        var requestId = await CreatePendingDispositionRequestAsync(requester, documentId);

        using var response = await RejectDispositionRequestAsync(approver, documentId, requestId, decisionReason: "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- download blocked when Disposed ----

    [Fact]
    public async Task DownloadFile_is_blocked_after_disposition_is_approved_but_details_remain_accessible()
    {
        using var requester = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage, Permissions.Documents.Read);
        using var approver = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Dispose);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await SetRetentionPolicyAsync(requester, documentTypeId, 30);
        var documentId = await CreateDraftDocumentWithFileAsync(requester, documentTypeId);

        // No active hold, past its retention due date (registered with the default 1-year
        // expiry from CreateDraftDocumentWithFileAsync would not be due; instead archive is
        // not required by MoveToPendingDisposition, so drive expiry directly via a fresh
        // expired document that also carries a file for the download check).
        using (var correction = await requester.PutAsJsonAsync(
            $"/api/documents/{documentId}/metadata",
            new CorrectDocumentMetadataRequest(documentTypeId, $"CERT-{Guid.NewGuid():N}", LongExpiredDate.AddYears(-1), LongExpiredDate, "Flag State Authority"),
            CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.NoContent, correction.StatusCode);
        }

        var requestId = await CreatePendingDispositionRequestAsync(requester, documentId);
        using (var approve = await ApproveDispositionRequestAsync(approver, documentId, requestId))
        {
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        }

        using var downloadResponse = await requester.GetAsync($"/api/documents/{documentId}/file", CancellationToken.None);
        Assert.Equal(HttpStatusCode.Forbidden, downloadResponse.StatusCode);

        using var detailsResponse = await requester.GetAsync($"/api/documents/{documentId}", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
    }
}
