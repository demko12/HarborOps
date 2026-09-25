using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class DocumentReadinessPanelTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000004";
    private const string SelectedDocumentId = "10000000-0000-0000-0000-000000000001";
    private const string AlternativeDocumentId = "10000000-0000-0000-0000-000000000002";

    private const string UnconfiguredVesselId = "40000000-0000-0000-0000-000000000005";

    private readonly List<Uri> requestedUris = [];
    private readonly List<Uri> overviewUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToReadiness =
        _ => Json(HttpStatusCode.OK, Page(summary: (2, 1, 1), MissingRow(), SatisfiedRow()));

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToOverview =
        _ => Json(HttpStatusCode.OK, OverviewPage);

    [Fact]
    public void The_summary_distinguishes_configured_satisfied_and_unsatisfied()
    {
        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.Equal(
            ["2", "1", "1"],
            panel.FindAll(".readiness-kpi-value").Select(value => value.TextContent.Trim())));
    }

    [Fact]
    public void Blockers_are_shown_with_readable_labels()
    {
        respondToReadiness = _ => Json(HttpStatusCode.OK, Page(summary: (1, 0, 1), BlockedRow()));

        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.Equal(
            ["Not approved", "Insufficient validity"],
            panel.FindAll(".readiness-blocker").Select(blocker => blocker.TextContent)));
    }

    [Fact]
    public void A_vessel_without_requirements_is_shown_as_not_configured_not_ready()
    {
        respondToReadiness = _ => Json(HttpStatusCode.OK, VesselPage("NotConfigured", (0, 0, 0)));

        var panel = RenderPanel(initialVesselId: Guid.Parse(VesselId));

        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Not configured", panel.Find(".readiness-vessel-status").TextContent);
            Assert.Contains("No requirements configured for this vessel", panel.Markup, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(">Ready<", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Managers_get_a_register_shortcut_for_a_missing_type()
    {
        (Guid VesselId, string VesselName, int DocumentTypeId)? requested = null;
        var panel = RenderPanel(
            onRegisterMissing: missing => requested = missing);
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".readiness-documents .readiness-row-action")));

        panel.Find(".readiness-documents .readiness-row-action").Click();

        Assert.Equal((Guid.Parse(VesselId), "Aurora Borealis", 7), requested);
    }

    [Fact]
    public void Read_only_users_do_not_get_the_register_shortcut()
    {
        var panel = RenderPanel(permissions: "documents.read");

        panel.WaitForAssertion(() => Assert.Contains("Missing", panel.Markup, StringComparison.Ordinal));
        Assert.Empty(panel.FindAll(".readiness-documents .readiness-row-action"));
    }

    [Fact]
    public void The_selected_document_opens_the_document_details()
    {
        Guid? opened = null;
        var panel = RenderPanel(onOpenDocument: documentId => opened = documentId);
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".readiness-document-link")));

        panel.Find(".readiness-document-link").Click();

        Assert.Equal(Guid.Parse(SelectedDocumentId), opened);
    }

    [Fact]
    public void Alternatives_are_listed_on_request()
    {
        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".readiness-toggle")));

        Assert.Empty(panel.FindAll(".readiness-alternatives"));

        panel.Find(".readiness-toggle").Click();

        Assert.Contains("CERT-002", panel.Find(".readiness-alternatives").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Distribution_progress_is_shown_for_the_selected_document()
    {
        var panel = RenderPanel();

        panel.WaitForAssertion(() =>
        {
            var counts = panel.Find(".readiness-distribution-counts").TextContent;
            Assert.Contains("2 acknowledged", counts, StringComparison.Ordinal);
            Assert.Contains("1 pending", counts, StringComparison.Ordinal);
            Assert.Contains("1 overdue", counts, StringComparison.Ordinal);
        });
        Assert.Empty(panel.FindAll(".readiness-recipients"));
    }

    [Fact]
    public void Recipients_are_listed_on_request_when_the_server_returns_them()
    {
        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".readiness-distribution .readiness-toggle")));

        panel.Find(".readiness-distribution .readiness-toggle").Click();

        var recipients = panel.Find(".readiness-recipients").TextContent;
        Assert.Contains("chief.engineer@harborops.test", recipients, StringComparison.Ordinal);
        Assert.Contains("Overdue", recipients, StringComparison.Ordinal);
        Assert.Contains("Acknowledged 21 Sep 2026", recipients, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_whose_current_revision_was_never_issued_shows_not_issued()
    {
        respondToReadiness = _ => Json(HttpStatusCode.OK, Page(summary: (1, 1, 0), SatisfiedRow(
            """{"status":"NotIssued","issueId":null,"issuedAtUtc":null,"dueDate":null,"pendingCount":0,"acknowledgedCount":0,"overdueCount":0,"recipients":[]}""")));

        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.Equal("Not issued", panel.Find(".readiness-distribution-status").TextContent));
    }

    [Fact]
    public void Filters_are_sent_to_the_readiness_query()
    {
        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".readiness-documents .readiness-row-action")));

        panel.Find("#readiness-status").Change("Unsatisfied");
        panel.Find("#readiness-required-through").Change("2026-12-31");

        panel.WaitForAssertion(() =>
        {
            var query = requestedUris.Last().Query;
            Assert.Contains("status=Unsatisfied", query, StringComparison.Ordinal);
            Assert.Contains("requiredThrough=2026-12-31", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Server_validation_errors_are_shown_above_the_matrix()
    {
        respondToReadiness = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"requiredThrough":["Required-through date must be between today and 365 days ahead."]}}""");

        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.Contains(
            "Required-through date must be between today and 365 days ahead.",
            panel.Find(".readiness-validation").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_load_can_be_retried()
    {
        var failing = true;
        respondToReadiness = _ => failing
            ? Json(HttpStatusCode.ServiceUnavailable, """{"title":"Readiness unavailable","detail":"Try again."}""")
            : Json(HttpStatusCode.OK, Page(summary: (2, 1, 1), MissingRow(), SatisfiedRow()));

        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.Contains("Readiness unavailable", panel.Markup, StringComparison.Ordinal));

        failing = false;
        panel.Find(".api-error-state button").Click();

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".readiness-status")));
    }

    [Fact]
    public void A_new_refresh_token_reloads_the_matrix()
    {
        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.Single(requestedUris));

        panel.Render(parameters => parameters.Add(component => component.RefreshToken, 1));

        panel.WaitForAssertion(() => Assert.Equal(2, requestedUris.Count));
        Assert.Equal(2, overviewUris.Count);
    }

    [Fact]
    public void Recipient_state_changes_show_their_date_and_reason()
    {
        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".readiness-distribution .readiness-toggle")));

        panel.Find(".readiness-distribution .readiness-toggle").Click();

        var recipients = panel.Find(".readiness-recipients").TextContent;
        Assert.Contains("Obsolete 22 Sep 2026", recipients, StringComparison.Ordinal);
        Assert.Equal("The document file was replaced.", panel.Find(".readiness-recipient-reason").TextContent);
    }

    [Fact]
    public void The_vessel_overview_lists_every_vessel_with_its_own_status()
    {
        var panel = RenderPanel();

        panel.WaitForAssertion(() =>
        {
            var rows = panel.FindAll(".readiness-vessel-table tbody tr");
            Assert.Equal(2, rows.Count);
            Assert.Contains("Aurora Borealis", rows[0].TextContent, StringComparison.Ordinal);
            Assert.Equal("Not ready", rows[0].QuerySelector(".readiness-vessel-status")!.TextContent);
            Assert.Contains("Borealis Two", rows[1].TextContent, StringComparison.Ordinal);
            Assert.Equal("Not configured", rows[1].QuerySelector(".readiness-vessel-status")!.TextContent);
        });
    }

    [Fact]
    public void Viewing_a_vessel_from_the_overview_filters_the_matrix_to_that_vessel()
    {
        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll(".readiness-vessel-table tbody tr").Count));

        panel.FindAll(".readiness-vessel-table tbody tr")[1].QuerySelector("button")!.Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains($"vesselId={UnconfiguredVesselId}", requestedUris.Last().Query, StringComparison.Ordinal);
            Assert.Empty(panel.FindAll(".readiness-vessel-table"));
            Assert.Equal("Borealis Two", panel.Find("#readiness-vessel").GetAttribute("value"));
        });
    }

    [Fact]
    public void The_vessel_overview_is_not_requested_for_a_single_vessel()
    {
        var panel = RenderPanel(initialVesselId: Guid.Parse(VesselId));

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".readiness-status")));
        Assert.Empty(overviewUris);
        Assert.Empty(panel.FindAll(".readiness-vessels"));
    }

    [Fact]
    public void The_vessel_overview_sends_the_required_through_filter()
    {
        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.Single(overviewUris));

        panel.Find("#readiness-required-through").Change("2026-12-31");

        panel.WaitForAssertion(() => Assert.Contains("requiredThrough=2026-12-31", overviewUris.Last().Query, StringComparison.Ordinal));
    }

    [Fact]
    public void Both_sections_show_a_loading_state_until_their_responses_arrive()
    {
        var gate = new TaskCompletionSource();
        respondToReadiness = async _ =>
        {
            await gate.Task;

            return await Json(HttpStatusCode.OK, Page(summary: (2, 1, 1), MissingRow(), SatisfiedRow()));
        };
        respondToOverview = async _ =>
        {
            await gate.Task;

            return await Json(HttpStatusCode.OK, OverviewPage);
        };

        var panel = RenderPanel();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("Loading document readiness...", panel.Markup, StringComparison.Ordinal);
            Assert.Contains("Loading vessel readiness...", panel.Markup, StringComparison.Ordinal);
        });

        gate.SetResult();

        panel.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading", panel.Markup, StringComparison.Ordinal);
            Assert.NotEmpty(panel.FindAll(".readiness-status"));
            Assert.NotEmpty(panel.FindAll(".readiness-vessel-table"));
        });
    }

    [Fact]
    public void A_failed_vessel_overview_can_be_retried()
    {
        var failing = true;
        respondToOverview = _ => failing
            ? Json(HttpStatusCode.ServiceUnavailable, """{"title":"Vessel readiness unavailable","detail":"Try again."}""")
            : Json(HttpStatusCode.OK, OverviewPage);

        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.Contains("Vessel readiness unavailable", panel.Find(".readiness-vessels").TextContent, StringComparison.Ordinal));

        failing = false;
        panel.Find(".readiness-vessels .api-error-state button").Click();

        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll(".readiness-vessel-table tbody tr").Count));
    }

    private IRenderedComponent<DocumentReadinessPanel> RenderPanel(
        Action<(Guid VesselId, string VesselName, int DocumentTypeId)>? onRegisterMissing = null,
        Action<Guid>? onOpenDocument = null,
        Guid? initialVesselId = null,
        params string[] permissions)
    {
        var documentsHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/documents/readiness/vessels")
            {
                overviewUris.Add(request.RequestUri);

                return respondToOverview(request);
            }

            requestedUris.Add(request.RequestUri);

            return respondToReadiness(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""")))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new DocumentsApiClient(documentsHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["documents.read", "documents.manage"] : permissions));

        var panel = Render<DocumentReadinessPanel>(parameters => parameters
            .Add(component => component.InitialVesselId, initialVesselId)
            .Add(component => component.OnRegisterMissing, EventCallback.Factory.Create<(Guid VesselId, string VesselName, int DocumentTypeId)>(this, missing => onRegisterMissing?.Invoke(missing)))
            .Add(component => component.OnOpenDocument, EventCallback.Factory.Create<Guid>(this, documentId => onOpenDocument?.Invoke(documentId))));
        panel.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return panel;
    }

    private static string MissingRow() =>
        $$"""
        {"requirementId":"11111111-1111-1111-1111-111111111111","vesselId":"{{VesselId}}","documentTypeId":7,
        "documentTypeName":"Safety Management Certificate","minimumRemainingValidityDays":0,"instruction":null,
        "requiredValidUntil":"2026-09-24","status":"Unsatisfied","blockers":["Missing"],"selectedDocumentId":null,"candidates":[]}
        """;

    private static string SatisfiedRow(string distribution = IssuedDistribution) =>
        $$"""
        {"requirementId":"22222222-2222-2222-2222-222222222222","vesselId":"{{VesselId}}","documentTypeId":8,
        "documentTypeName":"Load Line Certificate","minimumRemainingValidityDays":30,"instruction":"Original on board.",
        "requiredValidUntil":"2026-10-24","status":"Satisfied","blockers":[],"selectedDocumentId":"{{SelectedDocumentId}}",
        "candidates":[
        {"documentId":"{{SelectedDocumentId}}","number":"CERT-001","status":"Active","reviewStatus":"Approved","hasFile":true,"retentionState":"Active","issueDate":"2026-01-01","expiryDate":"2028-01-01","qualifies":true,"blockers":[]},
        {"documentId":"{{AlternativeDocumentId}}","number":"CERT-002","status":"Active","reviewStatus":"Approved","hasFile":true,"retentionState":"Active","issueDate":"2026-01-01","expiryDate":"2027-06-01","qualifies":true,"blockers":[]}],
        "distribution":{{distribution}}}
        """;

    private const string IssuedDistribution =
        """
        {"status":"Issued","issueId":"55555555-5555-5555-5555-555555555555","issuedAtUtc":"2026-09-20T08:00:00Z","dueDate":"2026-09-30","pendingCount":1,"acknowledgedCount":2,"overdueCount":1,
        "recipients":[{"userId":"66666666-6666-6666-6666-666666666661","email":"master@harborops.test","state":"Acknowledged","isOverdue":false,"acknowledgedAtUtc":"2026-09-21T09:00:00Z"},{"userId":"66666666-6666-6666-6666-666666666662","email":"chief.engineer@harborops.test","state":"Pending","isOverdue":true,"acknowledgedAtUtc":null},{"userId":"66666666-6666-6666-6666-666666666663","email":"bosun@harborops.test","state":"Obsolete","isOverdue":false,"acknowledgedAtUtc":null,"stateChangedAtUtc":"2026-09-22T10:00:00Z","stateReason":"The document file was replaced."}]}
        """;

    private const string OverviewPage =
        $$$"""
        {"today":"2026-09-24","requiredThrough":"2026-09-24",
        "items":[
        {"vesselId":"{{{VesselId}}}","vesselName":"Aurora Borealis","status":"NotReady","summary":{"configuredCount":2,"satisfiedCount":1,"unsatisfiedCount":1}},
        {"vesselId":"{{{UnconfiguredVesselId}}}","vesselName":"Borealis Two","status":"NotConfigured","summary":{"configuredCount":0,"satisfiedCount":0,"unsatisfiedCount":0}}],
        "pageNumber":1,"pageSize":25,"totalCount":2}
        """;

    private static string BlockedRow() =>
        $$"""
        {"requirementId":"33333333-3333-3333-3333-333333333333","vesselId":"{{VesselId}}","documentTypeId":9,
        "documentTypeName":"Safety Radio Certificate","minimumRemainingValidityDays":0,"instruction":null,
        "requiredValidUntil":"2026-09-24","status":"Unsatisfied","blockers":["NotApproved","InsufficientValidity"],"selectedDocumentId":null,"candidates":[]}
        """;

    private static string Page((int Configured, int Satisfied, int Unsatisfied) summary, params string[] rows) =>
        VesselPage(null, summary, rows);

    private static string VesselPage(string? vesselStatus, (int Configured, int Satisfied, int Unsatisfied) summary, params string[] rows) =>
        $$"""
        {"vesselId":{{(vesselStatus is null ? "null" : $"\"{VesselId}\"")}},"vesselStatus":{{(vesselStatus is null ? "null" : $"\"{vesselStatus}\"")}},
        "today":"2026-09-24","requiredThrough":"2026-09-24",
        "summary":{"configuredCount":{{summary.Configured}},"satisfiedCount":{{summary.Satisfied}},"unsatisfiedCount":{{summary.Unsatisfied}}},
        "items":[{{string.Join(",", rows)}}],"pageNumber":1,"pageSize":25,"totalCount":{{rows.Length}}}
        """;

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
