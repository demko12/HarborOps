using System.Net;
using System.Net.Http.Json;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using HarborOps.Modules.Identity.Contracts;

namespace HarborOps.IntegrationTests.Documents;

using static DocumentReviewWorkflowTestSupport;

/// <summary>
/// Covers the SQL-level read-only visibility filter on the two listing endpoints the Web
/// page actually uses (<c>/api/documents/expiring</c>) and its sibling
/// (<c>/api/documents</c>) - both must apply the filter before paginating/counting, not
/// just after, so TotalCount reflects what the caller can actually see.
/// </summary>
public sealed class DocumentListVisibilityEndpointTests : DocumentFileStorageIntegrationTestBase
{
    private readonly DocumentFileStorageApiFactory _application;

    public DocumentListVisibilityEndpointTests(DocumentFileStorageApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_list_manage_sees_all_documents_read_only_sees_only_approved()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);

        await CreateApprovedDocumentAsync(submitter, reviewer, documentTypeId);
        await CreateApprovedDocumentAsync(submitter, reviewer, documentTypeId);
        await CreatePendingReviewDocumentAsync(submitter, documentTypeId);
        await CreateRejectedDocumentAsync(submitter, reviewer, documentTypeId);
        await CreateDraftDocumentWithFileAsync(submitter, documentTypeId);

        using var manageResponse = await submitter.GetAsync("/api/documents", CancellationToken.None);
        var managePage = await manageResponse.Content.ReadFromJsonAsync<GetDocumentsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, manageResponse.StatusCode);
        Assert.Equal(5, managePage!.TotalCount);
        Assert.Equal(5, managePage.Items.Count);

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var readResponse = await readClient.GetAsync("/api/documents", CancellationToken.None);
        var readPage = await readResponse.Content.ReadFromJsonAsync<GetDocumentsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(2, readPage!.TotalCount);
        Assert.Equal(2, readPage.Items.Count);
        Assert.All(readPage.Items, item => Assert.Equal(Document.ApprovedReviewStatus, item.ReviewStatus));
    }

    [Fact]
    public async Task Get_expiring_manage_sees_all_documents_read_only_sees_only_approved()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);

        await CreateApprovedDocumentAsync(submitter, reviewer, documentTypeId);
        await CreateApprovedDocumentAsync(submitter, reviewer, documentTypeId);
        await CreatePendingReviewDocumentAsync(submitter, documentTypeId);
        await CreateRejectedDocumentAsync(submitter, reviewer, documentTypeId);
        await CreateDraftDocumentWithFileAsync(submitter, documentTypeId);

        using var manageResponse = await submitter.GetAsync("/api/documents/expiring", CancellationToken.None);
        var managePage = await manageResponse.Content.ReadFromJsonAsync<GetExpiringDocumentsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, manageResponse.StatusCode);
        Assert.Equal(5, managePage!.TotalCount);
        Assert.Equal(5, managePage.Items.Count);

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var readResponse = await readClient.GetAsync("/api/documents/expiring", CancellationToken.None);
        var readPage = await readResponse.Content.ReadFromJsonAsync<GetExpiringDocumentsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(2, readPage!.TotalCount);
        Assert.Equal(2, readPage.Items.Count);
        Assert.All(readPage.Items, item => Assert.Equal(Document.ApprovedReviewStatus, item.ReviewStatus));
    }

    [Fact]
    public async Task Get_expiring_with_review_status_filter_returns_only_matching_documents()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);

        await CreateApprovedDocumentAsync(submitter, reviewer, documentTypeId);
        var pendingReviewId = await CreatePendingReviewDocumentAsync(submitter, documentTypeId);

        using var response = await submitter.GetAsync(
            "/api/documents/expiring?reviewStatus=PendingReview", CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetExpiringDocumentsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single(page!.Items);
        Assert.Equal(pendingReviewId, item.DocumentId);
    }

    [Fact]
    public async Task Get_expiring_with_review_status_filter_and_read_only_permission_returns_empty_list()
    {
        // Filters AND-combine: a read-only caller is already restricted to Approved, so
        // explicitly asking for PendingReview (a state that caller can never see) correctly
        // yields nothing rather than silently ignoring the read-only restriction.
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        await CreatePendingReviewDocumentAsync(submitter, documentTypeId);

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await readClient.GetAsync(
            "/api/documents/expiring?reviewStatus=PendingReview", CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetExpiringDocumentsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(page!.Items);
        Assert.Equal(0, page.TotalCount);
    }
}
