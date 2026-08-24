using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Reports;
using HarborOps.Web.Components.Pages.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class ReportsPageTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000004";

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToDocumentsExpiry =
        _ => Json(HttpStatusCode.OK, DocumentsPage());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToComplianceExposure =
        _ => Json(HttpStatusCode.OK, CompliancePage());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMaintenanceBacklog =
        _ => Json(HttpStatusCode.OK, MaintenancePage());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToLookup =
        _ => Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToVesselSearch =
        _ => Json(HttpStatusCode.OK, VesselSearchPage());

    public ReportsPageTests()
    {
        ConfigureAuthorization(
            CurrentUserAuthorizationState.ReportsReadPermission,
            CurrentUserAuthorizationState.DocumentsReadPermission,
            CurrentUserAuthorizationState.ComplianceReadPermission,
            CurrentUserAuthorizationState.MaintenanceReadPermission);
    }

    [Fact]
    public void Initial_state_defaults_to_the_documents_expiry_tab_and_loads_it()
    {
        var page = RenderReportsPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requestedUris, IsDocumentsExpiryRequest);
            Assert.Contains("active", page.Find(".workspace-tab").ClassList);
            Assert.Contains("asOf=", requestedUris.Last(IsDocumentsExpiryRequest).Query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_user_without_documents_access_defaults_to_compliance_exposure()
    {
        ConfigureAuthorization(
            CurrentUserAuthorizationState.ReportsReadPermission,
            CurrentUserAuthorizationState.ComplianceReadPermission,
            CurrentUserAuthorizationState.MaintenanceReadPermission);

        var page = RenderReportsPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requestedUris, IsComplianceExposureRequest);
            Assert.DoesNotContain(requestedUris, IsDocumentsExpiryRequest);
        });
    }

    [Fact]
    public void A_user_with_no_module_read_permission_sees_no_reports_and_makes_no_requests()
    {
        ConfigureAuthorization(CurrentUserAuthorizationState.ReportsReadPermission);

        var page = RenderReportsPageWithoutWaiting();

        Assert.Contains("No reports available", page.Markup, StringComparison.Ordinal);
        Assert.Empty(requestedUris);
        Assert.Empty(page.FindAll(".workspace-tab"));
    }

    [Fact]
    public void Switching_tabs_loads_the_newly_selected_report()
    {
        var page = RenderReportsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsDocumentsExpiryRequest));

        page.FindAll(".workspace-tab")[1].Click();

        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsComplianceExposureRequest));
    }

    [Fact]
    public void Rows_are_shown_with_vessel_names_resolved_through_the_fleet_lookup()
    {
        respondToDocumentsExpiry = _ => Json(
            HttpStatusCode.OK, DocumentsPage(DocumentRow("10000000-0000-0000-0000-000000000001")));

        var page = RenderReportsPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Aurora Borealis", page.Markup, StringComparison.Ordinal);
            Assert.Contains("CERT-001", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_overdue_document_shows_expired_status_and_a_days_overdue_label_instead_of_a_negative_number()
    {
        respondToDocumentsExpiry = _ => Json(
            HttpStatusCode.OK,
            DocumentsPage(DocumentRow(
                "10000000-0000-0000-0000-000000000001",
                status: "Active",
                daysUntilExpiry: -298,
                isExpired: true)));

        var page = RenderReportsPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Expired", page.Markup, StringComparison.Ordinal);
            Assert.Contains("298 days overdue", page.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("-298", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_document_not_yet_expired_shows_a_days_left_label()
    {
        respondToDocumentsExpiry = _ => Json(
            HttpStatusCode.OK,
            DocumentsPage(DocumentRow(
                "10000000-0000-0000-0000-000000000001",
                status: "Active",
                daysUntilExpiry: 57,
                isExpired: false)));

        var page = RenderReportsPage();

        page.WaitForAssertion(() =>
            Assert.Contains("57 days left", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_data_shows_the_empty_state_instead_of_a_table()
    {
        var page = RenderReportsPage();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("table.reports-table"));
            Assert.Contains("No records match your filters", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_unreachable_api_shows_the_error_state_with_a_retry_action()
    {
        respondToDocumentsExpiry = _ => throw new HttpRequestException("Connection failed.");

        var page = RenderReportsPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll("table.reports-table"));
            Assert.NotNull(page.Find(".api-error-state button"));
        });
    }

    [Fact]
    public void Retry_reloads_the_report_after_a_failure()
    {
        var attempt = 0;
        respondToDocumentsExpiry = _ =>
        {
            attempt++;

            return attempt == 1
                ? throw new HttpRequestException("Connection failed.")
                : Json(HttpStatusCode.OK, DocumentsPage(DocumentRow("10000000-0000-0000-0000-000000000001", number: "CERT-777")));
        };

        var page = RenderReportsPage();
        page.WaitForAssertion(() => Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal));

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Contains("CERT-777", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Forbidden_response_shows_a_report_access_denied_message()
    {
        respondToDocumentsExpiry = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

        var page = RenderReportsPage();

        page.WaitForAssertion(() =>
            Assert.Contains("Report access denied", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Changing_a_filter_automatically_applies_it_and_resets_to_page_one()
    {
        var page = RenderReportsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsDocumentsExpiryRequest));

        page.Find("#reports-as-of").Change("2026-08-15");
        page.Find("#reports-doc-from").Change("2026-08-01");
        page.Find("#reports-doc-to").Change("2026-08-31");
        page.Find("#reports-doc-status").Change("Active");
        page.Find(".reports-filters .vessel-picker-input").Input("Auro");

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".reports-filters .vessel-picker-option")),
            TimeSpan.FromSeconds(3));

        // Find and Click are combined into one dispatcher-scheduled action: the debounce
        // timer that just rendered this option can trigger another render at any moment,
        // which would invalidate a separately-found IElement's event handler before a plain
        // .Find().Click() reaches it. Selecting the vessel is itself the last filter change
        // and fires the request that should reflect every field set above.
        await page.InvokeAsync(() => page.Find(".reports-filters .vessel-picker-option").Click());

        page.WaitForAssertion(
            () =>
            {
                var lastQuery = requestedUris.Last(IsDocumentsExpiryRequest).Query;

                Assert.Contains("asOf=2026-08-15", lastQuery, StringComparison.Ordinal);
                Assert.Contains("from=2026-08-01", lastQuery, StringComparison.Ordinal);
                Assert.Contains("to=2026-08-31", lastQuery, StringComparison.Ordinal);
                Assert.Contains("status=Active", lastQuery, StringComparison.Ordinal);
                Assert.Contains($"vesselId={VesselId}", lastQuery, StringComparison.Ordinal);
                Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Selecting_a_status_filter_applies_it_without_a_search_button()
    {
        var page = RenderReportsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsDocumentsExpiryRequest));

        Assert.Empty(page.FindAll(".reports-search-button"));

        page.Find("#reports-doc-status").Change("Active");

        page.WaitForAssertion(() =>
            Assert.Contains("status=Active", requestedUris.Last(IsDocumentsExpiryRequest).Query, StringComparison.Ordinal));
    }

    [Fact]
    public void A_to_date_before_the_from_date_shows_a_validation_message_without_calling_the_api()
    {
        var page = RenderReportsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsDocumentsExpiryRequest));

        // Setting "from" alone is a valid range on its own and legitimately triggers a
        // request - only the combination made below (to < from) should be blocked.
        page.Find("#reports-doc-from").Change("2026-06-01");
        page.WaitForAssertion(() => Assert.Contains("from=2026-06-01", requestedUris.Last(IsDocumentsExpiryRequest).Query, StringComparison.Ordinal));
        var requestsBeforeInvalidRange = requestedUris.Count(IsDocumentsExpiryRequest);

        page.Find("#reports-doc-to").Change("2026-01-01");

        Assert.Contains(
            "must be on or after",
            page.Find(".reports-validation").TextContent,
            StringComparison.Ordinal);
        Assert.Equal(requestsBeforeInvalidRange, requestedUris.Count(IsDocumentsExpiryRequest));
    }

    [Fact]
    public void Maintenance_backlog_filters_are_sent_including_priority_status_and_overdue()
    {
        var page = RenderReportsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsDocumentsExpiryRequest));

        page.FindAll(".workspace-tab")[2].Click();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsMaintenanceBacklogRequest));

        page.Find("#reports-priority").Change("Critical");
        page.Find("#reports-status").Change("Open");
        page.Find("#reports-overdue").Change("true");
        page.Find("#reports-due-from").Change("2026-08-01");
        page.Find("#reports-due-to").Change("2026-08-31");

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(IsMaintenanceBacklogRequest).Query;

            Assert.Contains("priority=Critical", lastQuery, StringComparison.Ordinal);
            Assert.Contains("status=Open", lastQuery, StringComparison.Ordinal);
            Assert.Contains("overdue=true", lastQuery, StringComparison.Ordinal);
            Assert.Contains("dueFrom=2026-08-01", lastQuery, StringComparison.Ordinal);
            Assert.Contains("dueTo=2026-08-31", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Compliance_exposure_category_filter_is_sent()
    {
        var page = RenderReportsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsDocumentsExpiryRequest));

        page.FindAll(".workspace-tab")[1].Click();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsComplianceExposureRequest));

        page.Find("#reports-category").Change("Insurance");

        page.WaitForAssertion(() =>
            Assert.Contains(
                "category=Insurance",
                requestedUris.Last(IsComplianceExposureRequest).Query,
                StringComparison.Ordinal));
    }

    [Fact]
    public void Reset_clears_filters_and_reloads_unfiltered_results()
    {
        var page = RenderReportsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsDocumentsExpiryRequest));

        page.Find("#reports-doc-from").Change("2026-08-01");
        page.Find("#reports-doc-to").Change("2026-08-31");
        page.WaitForAssertion(() =>
            Assert.Contains("from=2026-08-01", requestedUris.Last(IsDocumentsExpiryRequest).Query, StringComparison.Ordinal));

        page.Find(".workspace-filter-reset").Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(IsDocumentsExpiryRequest).Query;
            Assert.DoesNotContain("from=", lastQuery, StringComparison.Ordinal);
            Assert.DoesNotContain("to=", lastQuery, StringComparison.Ordinal);
            Assert.Null(page.Find("#reports-doc-from").GetAttribute("value"));
            Assert.Null(page.Find("#reports-doc-to").GetAttribute("value"));
        });
    }

    [Fact]
    public void Paging_requests_the_next_page_and_disables_previous_on_the_first_page()
    {
        respondToDocumentsExpiry = request =>
        {
            var pageNumber = request.RequestUri!.Query.Contains("pageNumber=2", StringComparison.Ordinal) ? 2 : 1;

            return Json(HttpStatusCode.OK, DocumentsPage(
                pageNumber,
                60,
                DocumentRow($"0000000{pageNumber}-0000-0000-0000-000000000001", number: $"Page {pageNumber}")));
        };

        var page = RenderReportsPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("table.reports-table")));

        var previous = page.FindAll(".reports-pages button")[0];
        Assert.True(previous.HasAttribute("disabled"));

        page.FindAll(".reports-pages button")[1].Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Page 2", page.Markup, StringComparison.Ordinal);
            Assert.False(page.FindAll(".reports-pages button")[0].HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Download_link_reflects_the_currently_applied_filters()
    {
        var page = RenderReportsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsDocumentsExpiryRequest));

        page.Find("#reports-as-of").Change("2026-08-15");
        page.Find("#reports-doc-status").Change("Active");

        page.WaitForAssertion(() =>
        {
            var href = page.Find(".reports-download-button").GetAttribute("href");
            Assert.Contains("/reports/download/documents-expiry", href, StringComparison.Ordinal);
            Assert.Contains("asOf=2026-08-15", href, StringComparison.Ordinal);
            Assert.Contains("status=Active", href, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Download_link_changes_when_switching_report_tabs()
    {
        var page = RenderReportsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsDocumentsExpiryRequest));

        Assert.Contains(
            "/reports/download/documents-expiry",
            page.Find(".reports-download-button").GetAttribute("href"),
            StringComparison.Ordinal);

        page.FindAll(".workspace-tab")[2].Click();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsMaintenanceBacklogRequest));

        Assert.Contains(
            "/reports/download/maintenance-backlog",
            page.Find(".reports-download-button").GetAttribute("href"),
            StringComparison.Ordinal);
    }

    private static bool IsDocumentsExpiryRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith("/documents-expiry", StringComparison.Ordinal);

    private static bool IsComplianceExposureRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith("/compliance-exposure", StringComparison.Ordinal);

    private static bool IsMaintenanceBacklogRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith("/maintenance-backlog", StringComparison.Ordinal);

    private void ConfigureAuthorization(params string[] permissions)
    {
        Services.RemoveAll<IHttpContextAccessor>();
        Services.RemoveAll<CurrentUserAuthorizationState>();
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    permissions
                        .Select(permission => new Claim(
                            CurrentUserAuthorizationState.PermissionClaimType,
                            permission))
                        .ToArray(),
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();
    }

    private IRenderedComponent<Reports> RenderReportsPage()
    {
        var page = RenderReportsPageWithoutWaiting();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private IRenderedComponent<Reports> RenderReportsPageWithoutWaiting()
    {
        var reportsHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return DispatchReports(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal)
                ? respondToLookup(request)
                : respondToVesselSearch(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new ReportsApiClient(reportsHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));

        return Render<Reports>();
    }

    private Task<HttpResponseMessage> DispatchReports(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/documents-expiry", StringComparison.Ordinal))
        {
            return respondToDocumentsExpiry(request);
        }

        if (path.EndsWith("/compliance-exposure", StringComparison.Ordinal))
        {
            return respondToComplianceExposure(request);
        }

        return respondToMaintenanceBacklog(request);
    }

    private static string DocumentRow(
        string documentId,
        string number = "CERT-001",
        string status = "Active",
        string documentTypeName = "Safety Certificate",
        string issuingAuthority = "Flag State",
        string issueDate = "2026-01-01",
        string expiryDate = "2026-12-31",
        int daysUntilExpiry = 120,
        bool isExpired = false)
    {
        return $$"""
            {"documentId":"{{documentId}}","vesselId":"{{VesselId}}","documentTypeId":1,
            "documentTypeName":"{{documentTypeName}}","number":"{{number}}",
            "issueDate":"{{issueDate}}","expiryDate":"{{expiryDate}}","issuingAuthority":"{{issuingAuthority}}",
            "status":"{{status}}","daysUntilExpiry":{{daysUntilExpiry}},"isExpired":{{(isExpired ? "true" : "false")}}}
            """;
    }

    private static string DocumentsPage(params string[] rows) => DocumentsPage(1, rows.Length, rows);

    private static string DocumentsPage(int pageNumber, int totalCount, params string[] rows)
    {
        return $$"""
            {"asOf":"2026-08-15","items":[{{string.Join(',', rows)}}],"pageNumber":{{pageNumber}},"pageSize":25,"totalCount":{{totalCount}}}
            """;
    }

    private static string CompliancePage(params string[] rows) => CompliancePage(1, rows.Length, rows);

    private static string CompliancePage(int pageNumber, int totalCount, params string[] rows)
    {
        return $$"""
            {"asOf":"2026-08-15","items":[{{string.Join(',', rows)}}],"pageNumber":{{pageNumber}},"pageSize":25,"totalCount":{{totalCount}}}
            """;
    }

    private static string MaintenancePage(params string[] rows) => MaintenancePage(1, rows.Length, rows);

    private static string MaintenancePage(int pageNumber, int totalCount, params string[] rows)
    {
        return $$"""
            {"asOf":"2026-08-15","items":[{{string.Join(',', rows)}}],"pageNumber":{{pageNumber}},"pageSize":25,"totalCount":{{totalCount}}}
            """;
    }

    private static string VesselSearchPage()
    {
        return $$"""
            {"items":[{"vesselId":"{{VesselId}}","name":"Aurora Borealis","imoNumber":"IMO1234567",
            "flagState":"Panama","currentStatus":"Active",
            "vesselTypeId":"60000000-0000-0000-0000-000000000006","vesselTypeName":"Tanker",
            "companyId":"70000000-0000-0000-0000-000000000007","dwt":null,
            "updatedAtUtc":"2026-01-01T00:00:00Z","lifecycleStage":null,"fleetId":null,"fleetName":null}],
            "pageNumber":1,"pageSize":10,"totalCount":1}
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
