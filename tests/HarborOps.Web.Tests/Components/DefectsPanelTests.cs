using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class DefectsPanelTests : BunitContext
{
    private const string VesselId = "30000000-0000-0000-0000-000000000002";
    private const string DefectId = "c0000000-0000-0000-0000-00000000000c";

    private readonly List<HttpRequestMessage> requestedRequests = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMaintenance =
        _ => Json(HttpStatusCode.OK, DefectsPage([Defect()]));

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToLookup =
        _ => Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""");

    [Fact]
    public void The_defect_list_is_rendered_with_the_looked_up_vessel_name()
    {
        var panel = RenderPanel();

        var row = panel.Find(".maintenance-work-order-row");

        Assert.Contains("Cracked bracket", row.TextContent, StringComparison.Ordinal);
        Assert.Equal("Aurora Borealis", row.QuerySelector(".col-vessel")!.TextContent.Trim());
        Assert.Contains("Reported", row.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_filter_narrows_the_request()
    {
        var panel = RenderPanel();

        panel.Find("#defects-status").Change("Triaged");

        panel.WaitForAssertion(() => Assert.Contains(
            "status=Triaged",
            LastDefectsQuery(),
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_severity_filter_narrows_the_request()
    {
        var panel = RenderPanel();

        panel.Find("#defects-severity").Change("Critical");

        panel.WaitForAssertion(() => Assert.Contains(
            "severity=Critical",
            LastDefectsQuery(),
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_vessel_filter_narrows_the_request()
    {
        var panel = RenderPanel();

        panel.Find(".vessel-picker-input").Focus();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".vessel-picker-option")));
        panel.Find(".vessel-picker-option").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            $"vesselId={VesselId}",
            LastDefectsQuery(),
            StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_result_set_shows_the_established_empty_state()
    {
        respondToMaintenance = _ => Json(HttpStatusCode.OK, DefectsPage());

        var panel = RenderPanel();

        Assert.Contains(
            "No defects match these filters",
            panel.Find(".empty-state").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Pagination_buttons_enable_and_disable_at_the_bounds_and_request_the_expected_page()
    {
        respondToMaintenance = request => Json(
            HttpStatusCode.OK,
            DefectsPage(items: [Defect()], totalCount: 30, pageNumber: PageNumberFrom(request)));

        var panel = RenderPanel();

        Assert.True(panel.Find(".maintenance-pages button:first-child").HasAttribute("disabled"));
        Assert.False(panel.Find(".maintenance-pages button:last-child").HasAttribute("disabled"));

        panel.Find(".maintenance-pages button:last-child").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("pageNumber=2", LastDefectsQuery(), StringComparison.Ordinal);
            Assert.False(panel.Find(".maintenance-pages button:first-child").HasAttribute("disabled"));
            Assert.True(panel.Find(".maintenance-pages button:last-child").HasAttribute("disabled"));
        });

        panel.Find(".maintenance-pages button:first-child").Click();

        panel.WaitForAssertion(() => Assert.Contains("pageNumber=1", LastDefectsQuery(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_later_filter_change_wins_over_a_still_in_flight_earlier_request()
    {
        // MaintenanceEquipmentPanelTests has no equivalent race test for EquipmentPanel
        // (only a dispose-cancels-the-request test) - this is the closest reasonable
        // equivalent for the loadVersion staleness guard on DefectsPanel.
        var firstRequestStarted = new TaskCompletionSource();
        var releaseFirstRequest = new TaskCompletionSource<HttpResponseMessage>();

        respondToMaintenance = request =>
        {
            if (!request.RequestUri!.Query.Contains("severity=", StringComparison.Ordinal))
            {
                firstRequestStarted.TrySetResult();

                return releaseFirstRequest.Task;
            }

            return Json(HttpStatusCode.OK, DefectsPage([Defect(title: "Fresh defect")]));
        };

        var panel = RenderPanel();

        await firstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        panel.Find("#defects-severity").Change("Critical");

        panel.WaitForAssertion(() => Assert.Contains("Fresh defect", panel.Markup, StringComparison.Ordinal));

        releaseFirstRequest.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                DefectsPage([Defect(title: "Stale defect")]),
                Encoding.UTF8,
                "application/json"),
        });

        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.Contains("Fresh defect", panel.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Stale defect", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Clicking_a_row_navigates_to_the_defect_details_page()
    {
        var panel = RenderPanel();

        panel.Find(".maintenance-work-order-row").Click();

        Assert.EndsWith(
            $"/maintenance/defects/{DefectId}",
            Services.GetRequiredService<NavigationManager>().Uri,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_report_defect_button_is_offered_with_the_manage_permission()
    {
        var panel = RenderPanel("maintenance.manage");

        var link = panel.Find(".maintenance-card-heading-actions a.maintenance-button");

        Assert.Equal("Report defect", link.TextContent.Trim());
    }

    [Fact]
    public void The_report_defect_button_is_absent_without_the_manage_permission()
    {
        var panel = RenderPanel("maintenance.read");

        Assert.Empty(panel.FindAll(".maintenance-card-heading-actions a.maintenance-button"));
    }

    [Fact]
    public void A_load_failure_shows_the_api_error_state_and_retry_reissues_the_load()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.InternalServerError,
            """{"title":"Unable to load defects","detail":"The register is offline."}""");

        var panel = RenderPanel();

        Assert.Contains("Unable to load defects", panel.Find(".api-error-state").TextContent, StringComparison.Ordinal);

        var requestsBeforeRetry = DefectsRequestCount();

        respondToMaintenance = _ => Json(HttpStatusCode.OK, DefectsPage([Defect()]));

        panel.Find(".api-error-state button").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.True(DefectsRequestCount() > requestsBeforeRetry);
            Assert.NotEmpty(panel.FindAll(".maintenance-work-order-row"));
        });
    }

    private int DefectsRequestCount()
    {
        return requestedRequests.Count(request => IsDefectsRequest(request));
    }

    private string LastDefectsQuery()
    {
        return requestedRequests.Last(IsDefectsRequest).RequestUri!.Query;
    }

    private static bool IsDefectsRequest(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath == "/api/maintenance/defects";
    }

    private static bool IsVesselLookup(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal);
    }

    private static string VesselSearchResults()
    {
        return $$"""
            {"items":[{"vesselId":"{{VesselId}}","name":"Aurora Borealis","imoNumber":"5100001",
            "currentStatus":"Active","fleetName":null,"flagState":"Panama","vesselTypeName":"Bulk carrier"}],
            "pageNumber":1,"pageSize":10,"totalCount":1}
            """;
    }

    private static string PageNumberFrom(HttpRequestMessage request)
    {
        var query = request.RequestUri!.Query;
        var marker = "pageNumber=";
        var start = query.IndexOf(marker, StringComparison.Ordinal);

        if (start < 0)
        {
            return "1";
        }

        start += marker.Length;
        var end = query.IndexOf('&', start);

        return end < 0 ? query[start..] : query[start..end];
    }

    private IRenderedComponent<DefectsPanel> RenderPanel(params string[] permissions)
    {
        var maintenanceHttpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            requestedRequests.Add(request);

            if (request.Content is not null)
            {
                await request.Content.ReadAsStringAsync(token);
            }

            return await respondToMaintenance(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
            IsVesselLookup(request)
                ? respondToLookup(request)
                : Json(HttpStatusCode.OK, VesselSearchResults())))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(maintenanceHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.manage"] : permissions));

        var panel = Render<DefectsPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(requestedRequests));

        return panel;
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

    private static string Defect(
        string defectId = DefectId,
        string title = "Cracked bracket",
        string status = "Reported",
        string reportedSeverity = "High",
        string confirmedSeverity = "null",
        string priority = "null")
    {
        return $$"""
            {"id":"{{defectId}}","vesselId":"{{VesselId}}","equipmentId":null,"title":"{{title}}",
            "reportedSeverity":"{{reportedSeverity}}","confirmedSeverity":{{confirmedSeverity}},
            "priority":{{priority}},"status":"{{status}}",
            "observedAtUtc":"2026-08-20T09:00:00Z","reportedAtUtc":"2026-08-20T10:00:00Z"}
            """;
    }

    private static string DefectsPage(
        IEnumerable<string>? items = null,
        int totalCount = -1,
        string pageNumber = "1")
    {
        var itemList = (items ?? []).ToArray();
        var count = totalCount == -1 ? itemList.Length : totalCount;

        return $$"""
            {"items":[{{string.Join(',', itemList)}}],"pageNumber":{{pageNumber}},"pageSize":25,
            "totalCount":{{count}}}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
