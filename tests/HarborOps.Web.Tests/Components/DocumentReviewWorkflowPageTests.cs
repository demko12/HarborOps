using System.Net;
using System.Security.Claims;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Documents;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class DocumentReviewWorkflowPageTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000004";
    private const string DocumentId = "10000000-0000-0000-0000-000000000001";
    private const string ActorId = "90000000-0000-0000-0000-000000000009";

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToExpiring =
        _ => Json(HttpStatusCode.OK, Page());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToDocumentTypes =
        _ => Json(HttpStatusCode.OK, """{"items":[],"pageSize":100,"totalCount":0}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToDocumentDetails =
        _ => Json(HttpStatusCode.OK, DocumentDetails());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToSubmitForReview =
        _ => NoContentAsync();

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToReview =
        _ => NoContentAsync();

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToRevoke =
        _ => NoContentAsync();

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToReviewHistory =
        _ => Json(HttpStatusCode.OK, "[]");

    // ---- Review-status badge ----

    [Theory]
    [InlineData("Draft", "documents-review-status-draft")]
    [InlineData("PendingReview", "documents-review-status-pending")]
    [InlineData("Approved", "documents-review-status-approved")]
    [InlineData("Rejected", "documents-review-status-rejected")]
    public void ReviewStatus_column_uses_the_matching_colored_badge(string reviewStatus, string expectedClass)
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId, reviewStatus: reviewStatus)));

        var page = RenderDocumentsPage();

        page.WaitForAssertion(() =>
            Assert.Contains(expectedClass, page.Find(".documents-review-status").ClassName, StringComparison.Ordinal));
    }

    // ---- Filter panel ----

    [Fact]
    public void Selecting_a_review_status_filter_sends_the_matching_query_parameter()
    {
        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        page.Find("#documents-review-status").Change("PendingReview");

        page.WaitForAssertion(
            () => Assert.Contains(
                requestedUris,
                uri => IsExpiringRequest(uri) && uri.Query.Contains("reviewStatus=PendingReview", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Selecting_the_all_option_sends_no_review_status_parameter()
    {
        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        page.Find("#documents-review-status").Change("Approved");
        page.WaitForAssertion(
            () => Assert.Contains(
                requestedUris,
                uri => IsExpiringRequest(uri) && uri.Query.Contains("reviewStatus=", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));

        page.Find("#documents-review-status").Change("");

        page.WaitForAssertion(
            () => Assert.DoesNotContain("reviewStatus=", requestedUris.Last(IsExpiringRequest).Query, StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Clearing_filters_resets_the_review_status_selection()
    {
        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        page.Find("#documents-review-status").Change("Approved");
        page.WaitForAssertion(
            () => Assert.Contains(
                requestedUris,
                uri => IsExpiringRequest(uri) && uri.Query.Contains("reviewStatus=Approved", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));

        page.Find(".documents-filter-reset").Click();

        page.WaitForAssertion(
            () => Assert.DoesNotContain("reviewStatus=", requestedUris.Last(IsExpiringRequest).Query, StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));
    }

    // ---- Permission-aware buttons ----

    [Fact]
    public void Manage_permission_shows_submit_for_review_only_for_draft_document_with_a_file()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(reviewStatus: "Draft", hasFile: true));

        var page = OpenFilePanel("documents.manage", "documents.read");

        page.WaitForAssertion(() =>
        {
            Assert.True(HasButtonWithText(page, "Submit for review"));
            Assert.False(HasButtonWithText(page, "Approve"));
            Assert.False(HasButtonWithText(page, "Reject"));
            Assert.False(HasButtonWithText(page, "Revoke"));
        });
    }

    [Fact]
    public void Manage_permission_shows_approve_and_reject_only_for_pending_review()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(reviewStatus: "PendingReview", hasFile: true));

        var page = OpenFilePanel("documents.manage", "documents.read");

        page.WaitForAssertion(() =>
        {
            Assert.True(HasButtonWithText(page, "Approve"));
            Assert.True(HasButtonWithText(page, "Reject"));
            Assert.False(HasButtonWithText(page, "Submit for review"));
            Assert.False(HasButtonWithText(page, "Revoke"));
        });
    }

    [Fact]
    public void Manage_permission_shows_revoke_only_for_active_and_approved()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(status: "Active", reviewStatus: "Approved", hasFile: true));

        var page = OpenFilePanel("documents.manage", "documents.read");

        page.WaitForAssertion(() =>
        {
            Assert.True(HasButtonWithText(page, "Revoke"));
            Assert.False(HasButtonWithText(page, "Submit for review"));
            Assert.False(HasButtonWithText(page, "Approve"));
        });
    }

    [Fact]
    public void Read_only_permission_hides_all_four_review_actions_regardless_of_state()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(status: "Active", reviewStatus: "PendingReview", hasFile: true));

        var page = OpenFilePanel("documents.read");

        page.WaitForAssertion(() =>
        {
            Assert.False(HasButtonWithText(page, "Submit for review"));
            Assert.False(HasButtonWithText(page, "Approve"));
            Assert.False(HasButtonWithText(page, "Reject"));
            Assert.False(HasButtonWithText(page, "Revoke"));
        });
    }

    // ---- Reject confirmation dialog (1:1 with Replace_file_shows_a_confirmation...) ----

    [Fact]
    public void Reject_button_opens_a_confirmation_form_and_sends_no_request()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(reviewStatus: "PendingReview", hasFile: true));

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Reject")));

        var requestsBefore = requestedUris.Count;
        FindButtonByText(page, "Reject").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#reject-notes")));
        Assert.Equal(requestsBefore, requestedUris.Count);
    }

    [Fact]
    public void Reject_with_blank_notes_shows_a_validation_error_and_sends_no_request()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(reviewStatus: "PendingReview", hasFile: true));

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Reject")));
        FindButtonByText(page, "Reject").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#reject-notes")));

        var requestsBefore = requestedUris.Count;
        FindButtonByText(page, "Confirm rejection").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("Notes are required when rejecting a document.", page.Markup, StringComparison.Ordinal));
        Assert.Equal(requestsBefore, requestedUris.Count);
    }

    [Fact]
    public void Reject_cancel_closes_the_form_without_a_request()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(reviewStatus: "PendingReview", hasFile: true));

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Reject")));
        FindButtonByText(page, "Reject").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#reject-notes")));

        var requestsBefore = requestedUris.Count;
        FindButtonByText(page, "Cancel").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("#reject-notes")));
        Assert.Equal(requestsBefore, requestedUris.Count);
    }

    [Fact]
    public void Reject_with_notes_sends_the_rejected_decision_and_notes()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(reviewStatus: "PendingReview", hasFile: true));
        string? capturedBody = null;
        respondToReview = async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();

            return await NoContentAsync();
        };

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Reject")));
        FindButtonByText(page, "Reject").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#reject-notes")));

        page.Find("#reject-notes").Change("Missing signature page.");
        FindButtonByText(page, "Confirm rejection").Click();

        page.WaitForAssertion(() => Assert.Contains("Document rejected.", page.Markup, StringComparison.Ordinal));
        Assert.NotNull(capturedBody);
        Assert.Contains("Rejected", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Missing signature page.", capturedBody, StringComparison.Ordinal);
    }

    // ---- Revoke confirmation dialog (same 1:1 structure) ----

    [Fact]
    public void Revoke_button_opens_a_confirmation_form_and_sends_no_request()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(status: "Active", reviewStatus: "Approved", hasFile: true));

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Revoke")));

        var requestsBefore = requestedUris.Count;
        FindButtonByText(page, "Revoke").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#revoke-reason")));
        Assert.Equal(requestsBefore, requestedUris.Count);
    }

    [Fact]
    public void Revoke_with_blank_reason_shows_a_validation_error_and_sends_no_request()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(status: "Active", reviewStatus: "Approved", hasFile: true));

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Revoke")));
        FindButtonByText(page, "Revoke").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#revoke-reason")));

        var requestsBefore = requestedUris.Count;
        FindButtonByText(page, "Confirm revoke").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("A reason is required to revoke a document.", page.Markup, StringComparison.Ordinal));
        Assert.Equal(requestsBefore, requestedUris.Count);
    }

    [Fact]
    public void Revoke_cancel_closes_the_form_without_a_request()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(status: "Active", reviewStatus: "Approved", hasFile: true));

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Revoke")));
        FindButtonByText(page, "Revoke").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#revoke-reason")));

        var requestsBefore = requestedUris.Count;
        FindButtonByText(page, "Cancel").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("#revoke-reason")));
        Assert.Equal(requestsBefore, requestedUris.Count);
    }

    [Fact]
    public void Revoke_with_reason_sends_the_reason()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(status: "Active", reviewStatus: "Approved", hasFile: true));
        string? capturedBody = null;
        respondToRevoke = async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();

            return await NoContentAsync();
        };

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Revoke")));
        FindButtonByText(page, "Revoke").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#revoke-reason")));

        page.Find("#revoke-reason").Change("No longer valid.");
        FindButtonByText(page, "Confirm revoke").Click();

        page.WaitForAssertion(() => Assert.Contains("Document revoked.", page.Markup, StringComparison.Ordinal));
        Assert.NotNull(capturedBody);
        Assert.Contains("No longer valid.", capturedBody, StringComparison.Ordinal);
    }

    // ---- Approve (no dialog) ----

    [Fact]
    public void Approve_sends_the_request_immediately_without_a_confirmation_dialog()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(reviewStatus: "PendingReview", hasFile: true));
        string? capturedBody = null;
        respondToReview = async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();

            return await NoContentAsync();
        };

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Approve")));

        FindButtonByText(page, "Approve").Click();

        page.WaitForAssertion(() => Assert.Contains("Document approved.", page.Markup, StringComparison.Ordinal));
        Assert.NotNull(capturedBody);
        Assert.Contains("Approved", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"notes\":null", capturedBody, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Review-history panel ----

    [Fact]
    public void Review_history_panel_does_not_load_until_expanded()
    {
        var page = OpenFilePanel();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-review-history-toggle")));

        Assert.DoesNotContain(requestedUris, IsReviewHistoryRequest);
    }

    [Fact]
    public void Expanding_review_history_loads_and_renders_events()
    {
        respondToReviewHistory = _ => Json(
            HttpStatusCode.OK,
            ReviewHistoryList(ReviewEvent("a0000000-0000-0000-0000-00000000000a", "Submitted", ActorId, "2026-08-18T10:00:00Z", null)));

        var page = OpenFilePanel();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-review-history-toggle")));

        page.Find(".documents-review-history-toggle").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requestedUris, IsReviewHistoryRequest);
            Assert.Contains("Submitted", page.Markup, StringComparison.Ordinal);
            Assert.Contains(ActorId, page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Empty_review_history_shows_the_no_history_message()
    {
        respondToReviewHistory = _ => Json(HttpStatusCode.OK, "[]");

        var page = OpenFilePanel();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-review-history-toggle")));
        page.Find(".documents-review-history-toggle").Click();

        page.WaitForAssertion(() => Assert.Contains("No review history yet.", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Approving_while_history_panel_is_expanded_refreshes_the_history()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(reviewStatus: "PendingReview", hasFile: true));
        var historyCallCount = 0;
        respondToReviewHistory = _ =>
        {
            historyCallCount++;

            return Json(HttpStatusCode.OK, "[]");
        };
        respondToReview = _ => NoContentAsync();

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-review-history-toggle")));
        page.Find(".documents-review-history-toggle").Click();
        page.WaitForAssertion(() => Assert.Equal(1, historyCallCount));

        FindButtonByText(page, "Approve").Click();

        page.WaitForAssertion(() => Assert.True(historyCallCount >= 2));
    }

    // ---- Success / conflict / retry ----

    [Fact]
    public void Approve_success_banner_remains_visible_after_a_rerender()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(reviewStatus: "PendingReview", hasFile: true));
        respondToReview = _ => NoContentAsync();

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Approve")));

        FindButtonByText(page, "Approve").Click();

        page.WaitForAssertion(() => Assert.Contains("Document approved.", page.Markup, StringComparison.Ordinal));

        // Regression guard for the banner-inside-a-closed-conditional bug: force another
        // render and confirm the message is still there, proving its block is not nested
        // inside a dialog conditional that the success path itself closes.
        page.Render();
        Assert.Contains("Document approved.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SelfReview_conflict_shows_the_server_message()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(reviewStatus: "PendingReview", hasFile: true));
        respondToReview = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"Document review conflict.","detail":"The reviewer must be different from the submitter.","errors":{}}""");

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Approve")));

        FindButtonByText(page, "Approve").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "The reviewer must be different from the submitter.",
            page.Find(".documents-type-inline-error").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Concurrency_conflict_shows_generic_message_and_refreshes_the_grid()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(reviewStatus: "PendingReview", hasFile: true));
        respondToReview = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"Document was modified concurrently.","detail":"The document was changed by another request. Reload and try again.","errors":{}}""");

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Approve")));

        var expiringRequestsBefore = requestedUris.Count(IsExpiringRequest);

        FindButtonByText(page, "Approve").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                "The document was changed by another request. Reload and try again.",
                page.Find(".documents-type-inline-error").TextContent,
                StringComparison.Ordinal);
            Assert.True(requestedUris.Count(IsExpiringRequest) > expiringRequestsBefore);
        });
    }

    [Fact]
    public void Network_failure_on_approve_shows_inline_error_and_keeps_the_button_clickable_for_retry()
    {
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(reviewStatus: "PendingReview", hasFile: true));
        respondToReview = _ => throw new HttpRequestException("Connection failed.");

        var page = OpenFilePanel("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.True(HasButtonWithText(page, "Approve")));

        FindButtonByText(page, "Approve").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "The documents service cannot be reached right now.",
            page.Find(".documents-type-inline-error").TextContent,
            StringComparison.Ordinal));

        var button = FindButtonByText(page, "Approve");
        Assert.False(button.HasAttribute("disabled"));
    }

    // ---- Disposal ----

    [Fact]
    public async Task Disposing_the_page_while_review_history_is_loading_does_not_throw()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        respondToReviewHistory = _ => pending.Task;

        var page = OpenFilePanel();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-review-history-toggle")));
        page.Find(".documents-review-history-toggle").Click();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsReviewHistoryRequest));

        await DisposeComponentsAsync();

        // The DocumentsApiClient.GetReviewHistoryAsync stub's continuation resumes here,
        // against an already-disposed component. Documents.razor.cs's new action/history
        // methods never call StateHasChanged/InvokeAsync directly (state is only mutated on
        // fields; Blazor's own post-event-handler render is what would normally follow), so
        // completing the pending task after disposal must not throw - xUnit fails this test
        // if it does.
        pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json"),
        });
    }

    // ---- helpers ----

    private static bool IsExpiringRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith("/expiring", StringComparison.Ordinal);

    private static bool IsReviewHistoryRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith("/review-history", StringComparison.Ordinal);

    private static IElement FindButtonByText(IRenderedComponent<Documents> page, string text) =>
        page.FindAll("button").Single(button => button.TextContent.Trim() == text);

    // The review-status filter <select> has option labels ("Approved", "Rejected", ...)
    // that are substrings of the action button labels ("Approve", "Reject"), so a plain
    // page.Markup.Contains(...) check is a false positive/negative trap here - this scopes
    // the check to actual <button> elements only, same set FindButtonByText queries.
    private static bool HasButtonWithText(IRenderedComponent<Documents> page, string text) =>
        page.FindAll("button").Any(button => button.TextContent.Trim() == text);

    private IRenderedComponent<Documents> OpenFilePanel(params string[] permissions)
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));

        var page = RenderDocumentsPage(permissions);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));
        page.Find(".documents-file-toggle").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-review-history-toggle")));

        return page;
    }

    private IRenderedComponent<Documents> RenderDocumentsPage(params string[] permissions)
    {
        var documentsHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return DispatchDocuments(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""")
                : Json(HttpStatusCode.OK, """{"items":[],"pageNumber":1,"pageSize":10,"totalCount":0}""");
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new DocumentsApiClient(documentsHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["documents.read", "documents.manage"] : permissions));

        var page = Render<Documents>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private static CurrentUserAuthorizationState CreateAuthorizationState(IEnumerable<string> permissions)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission));
        }

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return new CurrentUserAuthorizationState(new HttpContextAccessor { HttpContext = context });
    }

    private Task<HttpResponseMessage> DispatchDocuments(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/expiring", StringComparison.Ordinal))
        {
            return respondToExpiring(request);
        }

        if (path.EndsWith("/types", StringComparison.Ordinal))
        {
            return respondToDocumentTypes(request);
        }

        if (path.EndsWith("/review-history", StringComparison.Ordinal))
        {
            return respondToReviewHistory(request);
        }

        if (path.EndsWith("/submit-for-review", StringComparison.Ordinal))
        {
            return respondToSubmitForReview(request);
        }

        if (path.EndsWith("/revoke", StringComparison.Ordinal))
        {
            return respondToRevoke(request);
        }

        if (path.EndsWith("/review", StringComparison.Ordinal))
        {
            return respondToReview(request);
        }

        return respondToDocumentDetails(request);
    }

    private static string Row(
        string documentId,
        string number = "CERT-001",
        string status = "Active",
        string reviewStatus = "Draft",
        string documentTypeName = "Safety Certificate",
        string issuingAuthority = "Flag State",
        string issueDate = "2026-01-01",
        string expiryDate = "2026-12-31",
        int daysUntilExpiry = 120)
    {
        return $$"""
            {"documentId":"{{documentId}}","vesselId":"{{VesselId}}","number":"{{number}}",
            "issueDate":"{{issueDate}}","expiryDate":"{{expiryDate}}","issuingAuthority":"{{issuingAuthority}}",
            "status":"{{status}}","reviewStatus":"{{reviewStatus}}","documentTypeName":"{{documentTypeName}}",
            "daysUntilExpiry":{{daysUntilExpiry}}}
            """;
    }

    private static string Page(params string[] rows) =>
        $$"""{"items":[{{string.Join(',', rows)}}],"pageNumber":1,"pageSize":25,"totalCount":{{rows.Length}}}""";

    private static string DocumentDetails(
        string documentId = DocumentId,
        string status = "Active",
        string reviewStatus = "Draft",
        bool hasFile = false)
    {
        return $$"""
            {"id":"{{documentId}}","vesselId":"{{VesselId}}","documentTypeId":1,"documentTypeName":"Safety Certificate",
            "number":"CERT-001","issueDate":"2026-01-01","expiryDate":"2026-12-31","issuingAuthority":"Flag State",
            "status":"{{status}}","reviewStatus":"{{reviewStatus}}","hasFile":{{(hasFile ? "true" : "false")}},
            "fileName":{{(hasFile ? "\"cert.pdf\"" : "null")}},"contentType":{{(hasFile ? "\"application/pdf\"" : "null")}},
            "sizeBytes":{{(hasFile ? "2048" : "null")}},"uploadedBy":null,"uploadedAtUtc":null}
            """;
    }

    private static string ReviewEvent(string id, string action, string actorUserId, string occurredAtUtc, string? notes)
    {
        var notesJson = notes is null ? "null" : $"\"{notes}\"";

        return $$"""
            {"id":"{{id}}","documentId":"{{DocumentId}}","action":"{{action}}","actorUserId":"{{actorUserId}}",
            "occurredAtUtc":"{{occurredAtUtc}}","notes":{{notesJson}}}
            """;
    }

    private static string ReviewHistoryList(params string[] events) => $"[{string.Join(',', events)}]";

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });
    }

    private static Task<HttpResponseMessage> NoContentAsync() =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request);
        }
    }
}
