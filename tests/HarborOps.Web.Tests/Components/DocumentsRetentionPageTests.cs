using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Documents;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class DocumentsRetentionPageTests : BunitContext
{
    private const string DocumentId = "10000000-0000-0000-0000-000000000001";

    private readonly List<Uri> requestedUris = [];
    private readonly List<HttpRequestMessage> requestedMessages = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToExpiring =
        _ => Json(HttpStatusCode.OK, """{"items":[],"pageNumber":1,"pageSize":25,"totalCount":0}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToDocumentTypes =
        _ => Json(HttpStatusCode.OK, TypesPage(TypeRow()));

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToRetentionPolicies =
        _ => Json(HttpStatusCode.OK, """{"items":[]}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToUpdateRetentionPolicy =
        _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToRetentionQueue =
        _ => Json(HttpStatusCode.OK, QueuePage());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToArchive =
        _ => Json(HttpStatusCode.OK, """{"documentId":"10000000-0000-0000-0000-000000000001","state":"Archived"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToRestore =
        _ => Json(HttpStatusCode.OK, """{"documentId":"10000000-0000-0000-0000-000000000001","state":"Active"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToPlaceHold =
        _ => Json(HttpStatusCode.Created, """{"id":"20000000-0000-0000-0000-000000000002"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToReleaseHold =
        _ => Json(HttpStatusCode.OK, """{"id":"20000000-0000-0000-0000-000000000002","isActive":false}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToCreateDisposition =
        _ => Json(HttpStatusCode.Created, """{"id":"30000000-0000-0000-0000-000000000003","status":"Pending"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToApproveDisposition =
        _ => Json(HttpStatusCode.OK, """{"id":"30000000-0000-0000-0000-000000000003","status":"Approved"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToRejectDisposition =
        _ => Json(HttpStatusCode.OK, """{"id":"30000000-0000-0000-0000-000000000003","status":"Rejected"}""");

    // ---- Tab visibility ----

    [Fact]
    public void Retention_tab_is_hidden_without_documents_read_or_manage_permission()
    {
        var page = RenderDocumentsPage([]);

        Assert.DoesNotContain(page.FindAll(".workspace-tab"), el => el.TextContent.Contains("Retention", StringComparison.Ordinal));
    }

    [Fact]
    public void Retention_tab_is_visible_with_documents_read_permission()
    {
        var page = RenderDocumentsPage(["documents.read"]);

        Assert.Contains(page.FindAll(".workspace-tab"), el => el.TextContent.Contains("Retention", StringComparison.Ordinal));
    }

    // ---- Queue ----

    [Fact]
    public void Opening_the_retention_tab_loads_the_queue_and_renders_rows()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage(QueueRow()));

        var page = RenderDocumentsPage();
        OpenRetentionTab(page);

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requestedUris, IsRetentionQueueRequest);
            Assert.Contains("Active", page.Markup, StringComparison.Ordinal);
        });
    }

    // Regression test for the fire-and-forget bug: ToggleRetentionTab used to be a
    // synchronous `void` handler that kicked off LoadRetentionQueueAsync() as a detached
    // (`_ = ...`) task. Blazor only tracks - and re-renders after - the Task an event handler
    // itself returns, so once the fake HTTP response was made genuinely asynchronous (a
    // TaskCompletionSource that has not yet completed, rather than the other tests' already-
    // completed Task.FromResult), the completion of that detached task never triggered a
    // render and the spinner never cleared in the real app - even though every other test
    // here (using pre-completed fake responses) kept passing throughout, since `await` on an
    // already-completed Task continues synchronously and happened to finish before the
    // click handler itself returned, masking the bug. This test only passes now that
    // ToggleRetentionTab is `async Task` and Blazor awaits it directly.
    [Fact]
    public void Opening_the_retention_tab_clears_the_spinner_once_a_genuinely_async_response_arrives()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        respondToRetentionQueue = _ => pending.Task;

        var page = RenderDocumentsPage();
        OpenRetentionTab(page);

        page.WaitForAssertion(() =>
            Assert.Contains("Loading retention queue...", page.Markup, StringComparison.Ordinal));

        pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(QueuePage(QueueRow()), Encoding.UTF8, "application/json"),
        });

        page.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading retention queue...", page.Markup, StringComparison.Ordinal);
            Assert.Contains("Active", page.Markup, StringComparison.Ordinal);
        });
    }

    // Regression test for the "spinner never clears on error" class of bug: an ordinary
    // non-success HTTP response (DocumentsApiResult.IsSuccess == false, no exception
    // involved) must clear the spinner and show the error state instead of leaving it stuck.
    // (A malformed-body/JsonException path was also investigated: DocumentsApiClient does not
    // catch JsonException, so it propagates as a genuinely unhandled exception through
    // Blazor's event dispatch rather than a renderable failure state - the same pre-existing
    // gap the main grid's LoadAsync already has, which only catches OperationCanceledException.
    // LoadRetentionQueueAsync's try/finally still correctly clears isLoadingRetentionQueue
    // before that exception propagates, but there is no graceful markup to assert against in
    // that scenario, so it is not a meaningful bUnit assertion and is not included here.)
    [Fact]
    public void Opening_the_retention_tab_clears_the_spinner_when_the_request_returns_an_error()
    {
        respondToRetentionQueue = _ => Json(
            HttpStatusCode.InternalServerError,
            """{"title":"Unable to load the retention queue","errors":{}}""");

        var page = RenderDocumentsPage();
        OpenRetentionTab(page);

        page.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading retention queue...", page.Markup, StringComparison.Ordinal);
            Assert.NotEmpty(page.FindAll(".api-error-state"));
        });
    }

    [Fact]
    public void Empty_queue_shows_the_empty_state()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage());

        var page = RenderDocumentsPage();
        OpenRetentionTab(page);

        page.WaitForAssertion(() =>
            Assert.Contains("No documents are due for a retention decision", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void An_unreachable_retention_queue_shows_the_error_state_with_retry()
    {
        respondToRetentionQueue = _ => throw new HttpRequestException("Connection failed.");

        var page = RenderDocumentsPage();
        OpenRetentionTab(page);

        page.WaitForAssertion(() =>
            Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Changing_the_state_filter_reloads_the_queue_with_the_selected_state()
    {
        var page = RenderDocumentsPage();
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsRetentionQueueRequest));

        page.Find("#retention-state-filter").Change("Archived");

        page.WaitForAssertion(() =>
            Assert.Contains("state=Archived", requestedUris.Last(IsRetentionQueueRequest).Query, StringComparison.Ordinal));
    }

    [Fact]
    public void Reset_clears_the_retention_filters_and_reloads()
    {
        var page = RenderDocumentsPage();
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsRetentionQueueRequest));

        page.Find("#retention-state-filter").Change("Archived");
        page.WaitForAssertion(() =>
            Assert.Contains("state=Archived", requestedUris.Last(IsRetentionQueueRequest).Query, StringComparison.Ordinal));

        page.Find(".documents-retention-card .documents-filter-reset").Click();

        page.WaitForAssertion(() =>
            Assert.DoesNotContain("state=", requestedUris.Last(IsRetentionQueueRequest).Query, StringComparison.Ordinal));
    }

    // ---- Per-document actions panel ----

    [Fact]
    public void Manage_button_opens_the_actions_panel_for_that_document()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage(QueueRow()));

        var page = RenderDocumentsPage();
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));

        page.Find(".retention-manage-button").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-actions-panel")));
    }

    [Fact]
    public void Archive_button_is_hidden_without_documents_manage_permission()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage(QueueRow(state: "Active")));

        var page = RenderDocumentsPage(["documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));
        page.Find(".retention-manage-button").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-actions-panel")));
        Assert.Empty(page.FindAll(".retention-archive-button"));
    }

    [Fact]
    public void Archive_requires_confirmation_before_calling_the_api()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage(QueueRow(state: "Active")));

        var page = RenderDocumentsPage(["documents.manage", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));
        page.Find(".retention-manage-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-archive-button")));

        var requestsBeforeClick = requestedMessages.Count(IsArchiveRequest);
        page.Find(".retention-archive-button").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-confirm-archive-button")));
        Assert.Equal(requestsBeforeClick, requestedMessages.Count(IsArchiveRequest));
    }

    [Fact]
    public void Confirming_archive_calls_the_api_and_refreshes_the_queue()
    {
        var callCount = 0;
        respondToRetentionQueue = _ =>
        {
            callCount++;

            return Json(HttpStatusCode.OK, QueuePage(QueueRow(state: callCount > 1 ? "Archived" : "Active")));
        };

        var page = RenderDocumentsPage(["documents.manage", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));
        page.Find(".retention-manage-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-archive-button")));

        page.Find(".retention-archive-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-confirm-archive-button")));
        page.Find(".retention-confirm-archive-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requestedMessages, IsArchiveRequest);
            Assert.Contains("Document archived.", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_conflict_on_archive_shows_the_inline_error_and_does_not_close_the_confirmation()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage(QueueRow(state: "Active")));
        respondToArchive = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"Document cannot be archived.","detail":"Only an active document can be archived.","errors":{}}""");

        var page = RenderDocumentsPage(["documents.manage", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));
        page.Find(".retention-manage-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-archive-button")));
        page.Find(".retention-archive-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-confirm-archive-button")));

        page.Find(".retention-confirm-archive-button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("Only an active document can be archived.", page.Markup, StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll(".retention-confirm-archive-button"));
    }

    [Fact]
    public void Placing_a_legal_hold_with_a_blank_reason_shows_validation_without_calling_the_api()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage(QueueRow()));

        var page = RenderDocumentsPage(["documents.manage", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));
        page.Find(".retention-manage-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-place-hold-button")));

        page.Find(".retention-place-hold-button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("A reason is required to place a legal hold.", page.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(requestedMessages, IsPlaceHoldRequest);
    }

    [Fact]
    public void Placing_a_legal_hold_succeeds_and_refreshes_the_panel()
    {
        var callCount = 0;
        respondToRetentionQueue = _ =>
        {
            callCount++;

            return Json(HttpStatusCode.OK, QueuePage(QueueRow(activeHoldsCount: callCount > 1 ? 1 : 0)));
        };

        var page = RenderDocumentsPage(["documents.manage", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));
        page.Find(".retention-manage-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-place-hold-button")));

        page.Find("#hold-reason").Change("Litigation pending.");
        page.Find(".retention-place-hold-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requestedMessages, IsPlaceHoldRequest);
            Assert.Contains("Legal hold placed.", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Releasing_a_legal_hold_requires_a_reason()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage(QueueRow(activeHoldsCount: 1)));

        var page = RenderDocumentsPage(["documents.manage", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));
        page.Find(".retention-manage-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-release-button")));

        page.Find(".retention-release-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-confirm-release-button")));
        page.Find(".retention-confirm-release-button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("A reason is required to release a legal hold.", page.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(requestedMessages, IsReleaseHoldRequest);
    }

    [Fact]
    public void Requesting_disposition_succeeds_with_manage_permission()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage(QueueRow(state: "Active")));

        var page = RenderDocumentsPage(["documents.manage", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));
        page.Find(".retention-manage-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-request-disposition-button")));

        page.Find("#disposition-reason").Change("Retention period elapsed.");
        page.Find(".retention-request-disposition-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requestedMessages, IsCreateDispositionRequest);
            Assert.Contains("Disposition requested.", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Approve_and_reject_controls_are_hidden_without_documents_dispose_permission()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage(QueueRow(hasPendingDisposition: true)));

        var page = RenderDocumentsPage(["documents.manage", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));
        page.Find(".retention-manage-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Awaiting approval from a user with disposition authority.", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll(".retention-approve-button"));
            Assert.Empty(page.FindAll(".retention-reject-button"));
        });
    }

    [Fact]
    public void Approve_and_reject_controls_are_visible_with_documents_dispose_permission()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage(QueueRow(hasPendingDisposition: true)));

        var page = RenderDocumentsPage(["documents.dispose", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));
        page.Find(".retention-manage-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotEmpty(page.FindAll(".retention-approve-button"));
            Assert.NotEmpty(page.FindAll(".retention-reject-button"));
        });
    }

    [Fact]
    public void Approving_a_disposition_request_succeeds()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage(QueueRow(hasPendingDisposition: true)));

        var page = RenderDocumentsPage(["documents.dispose", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));
        page.Find(".retention-manage-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-approve-button")));

        page.Find(".retention-approve-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-confirm-approve-button")));
        page.Find(".retention-confirm-approve-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requestedMessages, IsApproveDispositionRequest);
            Assert.Contains("Disposition approved.", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Rejecting_a_disposition_request_requires_a_decision_reason()
    {
        respondToRetentionQueue = _ => Json(HttpStatusCode.OK, QueuePage(QueueRow(hasPendingDisposition: true)));

        var page = RenderDocumentsPage(["documents.dispose", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-manage-button")));
        page.Find(".retention-manage-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-reject-button")));

        page.Find(".retention-reject-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-confirm-reject-button")));
        page.Find(".retention-confirm-reject-button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("A decision reason is required to reject a disposition request.", page.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(requestedMessages, IsRejectDispositionRequest);
    }

    // ---- Retention policies ----

    [Fact]
    public void Retention_policies_sub_tab_is_hidden_without_documents_manage_permission()
    {
        var page = RenderDocumentsPage(["documents.read"]);
        OpenRetentionTab(page);

        page.WaitForAssertion(() =>
            Assert.DoesNotContain(
                page.FindAll(".documents-retention-card .workspace-tab"),
                el => el.TextContent.Contains("policies", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Retention_policies_sub_tab_lists_document_types_with_their_configured_period()
    {
        respondToRetentionPolicies = _ => Json(
            HttpStatusCode.OK,
            """{"items":[{"documentTypeId":1,"documentTypeName":"Safety Certificate","retentionPeriodDays":365}]}""");

        var page = RenderDocumentsPage(["documents.manage", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-retention-card .workspace-tab")));

        page.FindAll(".documents-retention-card .workspace-tab")[1].Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requestedUris, IsRetentionPoliciesGetRequest);
            Assert.Contains("Safety Certificate", page.Markup, StringComparison.Ordinal);
        });
        Assert.Equal("365", page.Find(".retention-period-input").GetAttribute("value"));
    }

    [Fact]
    public void Saving_a_retention_policy_sends_the_updated_period()
    {
        respondToRetentionPolicies = _ => Json(
            HttpStatusCode.OK,
            """{"items":[{"documentTypeId":1,"documentTypeName":"Safety Certificate","retentionPeriodDays":365}]}""");

        string? capturedBody = null;
        respondToUpdateRetentionPolicy = async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        };

        var page = RenderDocumentsPage(["documents.manage", "documents.read"]);
        OpenRetentionTab(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-retention-card .workspace-tab")));
        page.FindAll(".documents-retention-card .workspace-tab")[1].Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".retention-period-input")));

        page.Find(".retention-period-input").Change("730");
        page.Find(".retention-save-policy-button").Click();

        page.WaitForAssertion(() => Assert.NotNull(capturedBody));
        Assert.Contains("\"retentionPeriodDays\":730", capturedBody, StringComparison.Ordinal);
    }

    private static bool IsRetentionQueueRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith("/retention-queue", StringComparison.Ordinal);

    private static bool IsRetentionPoliciesGetRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith("/retention-policies", StringComparison.Ordinal);

    private static bool IsArchiveRequest(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/archive", StringComparison.Ordinal);

    private static bool IsPlaceHoldRequest(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/legal-holds", StringComparison.Ordinal);

    private static bool IsReleaseHoldRequest(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/release", StringComparison.Ordinal);

    private static bool IsCreateDispositionRequest(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/disposition-requests", StringComparison.Ordinal);

    private static bool IsApproveDispositionRequest(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/approve", StringComparison.Ordinal);

    private static bool IsRejectDispositionRequest(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/reject", StringComparison.Ordinal);

    private static void OpenRetentionTab(IRenderedComponent<Documents> page) =>
        page.FindAll(".workspace-tab").First(el => el.TextContent.Contains("Retention", StringComparison.Ordinal)).Click();

    private IRenderedComponent<Documents> RenderDocumentsPage(string[]? permissions = null)
    {
        var documentsHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);
            requestedMessages.Add(request);

            return Dispatch(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return Json(HttpStatusCode.OK, """{"vessels":[]}""");
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new DocumentsApiClient(documentsHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions is null ? ["documents.read", "documents.manage", "documents.dispose"] : permissions));

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

    private Task<HttpResponseMessage> Dispatch(HttpRequestMessage request)
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

        if (path.EndsWith("/retention-queue", StringComparison.Ordinal))
        {
            return respondToRetentionQueue(request);
        }

        if (path.EndsWith("/retention-policies", StringComparison.Ordinal))
        {
            return request.Method == HttpMethod.Get
                ? respondToRetentionPolicies(request)
                : respondToUpdateRetentionPolicy(request);
        }

        if (path.Contains("/retention-policies/", StringComparison.Ordinal))
        {
            return respondToUpdateRetentionPolicy(request);
        }

        if (path.EndsWith("/archive", StringComparison.Ordinal))
        {
            return respondToArchive(request);
        }

        if (path.EndsWith("/restore", StringComparison.Ordinal))
        {
            return respondToRestore(request);
        }

        if (path.EndsWith("/release", StringComparison.Ordinal))
        {
            return respondToReleaseHold(request);
        }

        if (path.EndsWith("/legal-holds", StringComparison.Ordinal))
        {
            return respondToPlaceHold(request);
        }

        if (path.EndsWith("/approve", StringComparison.Ordinal))
        {
            return respondToApproveDisposition(request);
        }

        if (path.EndsWith("/reject", StringComparison.Ordinal))
        {
            return respondToRejectDisposition(request);
        }

        if (path.EndsWith("/disposition-requests", StringComparison.Ordinal))
        {
            return respondToCreateDisposition(request);
        }

        return Json(HttpStatusCode.NotFound, """{"title":"Not found.","errors":{}}""");
    }

    private static string TypesPage(params string[] rows) =>
        $$"""{"items":[{{string.Join(',', rows)}}],"pageSize":100,"totalCount":{{rows.Length}}}""";

    private static string TypeRow(
        int documentTypeId = 1,
        string name = "Safety Certificate",
        int mandatoryValidityPeriodDays = 365,
        string issuingAuthority = "Flag State",
        bool requiresSurvey = true)
    {
        var requiresSurveyText = requiresSurvey ? "true" : "false";

        return $$"""
            {"documentTypeId":{{documentTypeId}},"name":"{{name}}",
            "mandatoryValidityPeriodDays":{{mandatoryValidityPeriodDays}},
            "issuingAuthority":"{{issuingAuthority}}","requiresSurvey":{{requiresSurveyText}}}
            """;
    }

    private static string QueuePage(params string[] rows) =>
        $$"""{"items":[{{string.Join(',', rows)}}],"pageNumber":1,"pageSize":25,"totalCount":{{rows.Length}}}""";

    private static string QueueRow(
        string documentId = DocumentId,
        int documentTypeId = 1,
        string state = "Active",
        int activeHoldsCount = 0,
        bool hasPendingDisposition = false)
    {
        var holds = string.Join(
            ',',
            Enumerable.Range(0, activeHoldsCount).Select(i =>
                $$"""{"id":"4000000{{i}}-0000-0000-0000-000000000004","reason":"Litigation.","placedAtUtc":"2026-08-01T00:00:00Z"}"""));

        var pendingDisposition = hasPendingDisposition
            ? """{"id":"30000000-0000-0000-0000-000000000003","requestedByUserId":"90000000-0000-0000-0000-000000000009","requestedAtUtc":"2026-08-05T00:00:00Z"}"""
            : "null";

        return $$"""
            {"documentId":"{{documentId}}","documentTypeId":{{documentTypeId}},
            "lifecycleTriggerAtUtc":"2026-01-01T00:00:00Z","retentionDueAtUtc":"2026-01-31T00:00:00Z",
            "retentionState":"{{state}}","activeHolds":[{{holds}}],"pendingDisposition":{{pendingDisposition}}}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });
    }

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
