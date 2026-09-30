using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarborOps.Web.Tests.Components;

public sealed class HomePageTests : BunitContext
{
    [Fact]
    public void Home_renders_summary_cards_attention_items_and_deep_links()
    {
        ConfigureClients("""
            {"asOf":"2026-08-17","sections":[{"capability":"fleet","title":"Fleet","summaryLabel":"Active vessels","count":4,"link":"/fleet?status=Active","items":[{"title":"MV Aurora","detail":"En Route","dueLabel":null,"severity":"normal"}],"isUnavailable":false}],"priorityItems":[{"capability":"fleet","capabilityTitle":"Fleet","title":"MV Aurora","detail":"En Route","dueLabel":null,"severity":"normal","link":"/fleet?status=Active"}]}
            """);

        var page = Render<Home>();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("What needs attention", page.Markup, StringComparison.Ordinal);
            Assert.Contains("Active vessels", page.Markup, StringComparison.Ordinal);
            Assert.Contains("Priority queue", page.Markup, StringComparison.Ordinal);
            Assert.Contains("Upcoming", page.Markup, StringComparison.Ordinal);
            Assert.Contains("MV Aurora", page.Markup, StringComparison.Ordinal);
            Assert.Contains("href=\"/fleet?status=Active\"", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Home_links_crew_priority_items_to_the_crew_member_profile()
    {
        var crewMemberId = "5c376ddd-cac5-4f68-85c0-5d798cf2a2ed";
        ConfigureClients($$"""
            {"asOf":"2026-08-17","sections":[{"capability":"crew","title":"Crew","summaryLabel":"Credentials expiring within 30 days","count":1,"link":"/crew","items":[{"title":"Jamie Carter","detail":"Passport expires","dueLabel":"2026-08-20","severity":"warning","link":"/crew/{{crewMemberId}}"}],"isUnavailable":false}],"priorityItems":[{"capability":"crew","capabilityTitle":"Crew","title":"Jamie Carter","detail":"Passport expires","dueLabel":"2026-08-20","severity":"warning","link":"/crew/{{crewMemberId}}"}]}
            """);

        var page = Render<Home>();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(3, page.FindAll($"a[href='/crew/{crewMemberId}']").Count);
            Assert.Contains("Jamie Carter", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Home_links_fleet_attention_and_priority_items_to_vessel_details()
    {
        var vesselId = "7c376ddd-cac5-4f68-85c0-5d798cf2a2ed";
        ConfigureClients($$"""
            {"asOf":"2026-08-17","sections":[{"capability":"fleet","title":"Fleet","summaryLabel":"Active vessels","count":1,"link":"/fleet?status=Active","items":[{"title":"MV Aurora","detail":"En Route","dueLabel":null,"severity":"normal","link":"/fleet/{{vesselId}}"}],"isUnavailable":false}],"priorityItems":[{"capability":"fleet","capabilityTitle":"Fleet","title":"MV Aurora","detail":"En Route","dueLabel":null,"severity":"normal","link":"/fleet/{{vesselId}}"}]}
            """);

        var page = Render<Home>();

        page.WaitForAssertion(() =>
            Assert.Equal(2, page.FindAll($"a[href='/fleet/{vesselId}']").Count));
    }

    [Fact]
    public void Home_links_maintenance_priority_and_preview_items_to_the_specific_work_order()
    {
        var workOrderId = "6c376ddd-cac5-4f68-85c0-5d798cf2a2ed";
        var detailsLink = $"/maintenance/work-orders/{workOrderId}";
        ConfigureClients($$"""
            {"asOf":"2026-08-17","sections":[{"capability":"maintenance","title":"Maintenance","summaryLabel":"Overdue or critical open work orders","count":1,"link":"/maintenance?overdue=true","items":[{"title":"Inspect engine","detail":"Critical · Open","dueLabel":"2026-08-16","severity":"critical","link":"{{detailsLink}}"}],"isUnavailable":false}],"priorityItems":[{"capability":"maintenance","capabilityTitle":"Maintenance","title":"Inspect engine","detail":"Critical · Open","dueLabel":"2026-08-16","severity":"critical","link":"{{detailsLink}}"}]}
            """);

        var page = Render<Home>();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(detailsLink, page.Find(".dashboard-priority-item").GetAttribute("href"));
            Assert.Equal(detailsLink, page.Find(".workspace-item").GetAttribute("href"));
            Assert.Equal("/maintenance?overdue=true", page.Find(".dashboard-summary-card").GetAttribute("href"));
        });
    }

    [Fact]
    public void Home_shows_an_empty_state_when_the_session_has_no_module_permissions()
    {
        ConfigureClients("""{"asOf":"2026-08-17","sections":[],"priorityItems":[]}""");

        var page = Render<Home>();

        page.WaitForAssertion(() => Assert.Contains("No operational access yet", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Home_keeps_healthy_sections_visible_when_another_section_is_unavailable()
    {
        ConfigureClients("""
            {"asOf":"2026-08-17","sections":[{"capability":"fleet","title":"Fleet","summaryLabel":"Active vessels","count":4,"link":"/fleet","items":[],"isUnavailable":false},{"capability":"crew","title":"Crew","summaryLabel":"Credentials expiring within 30 days","count":0,"link":"/crew","items":[],"isUnavailable":true}],"priorityItems":[]}
            """);

        var page = Render<Home>();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Some operational sections are temporarily unavailable", page.Markup, StringComparison.Ordinal);
            Assert.Contains("Fleet", page.Markup, StringComparison.Ordinal);
            Assert.Contains("Unavailable", page.Find("#module-tab-crew").InnerHtml, StringComparison.Ordinal);
        });
        page.Find("#module-tab-crew").Click();
        page.WaitForAssertion(() =>
        {
            Assert.Contains("This section could not be loaded", page.Find("#module-preview").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Home_filters_module_previews_and_can_clear_an_empty_search()
    {
        ConfigureClients("""
            {"asOf":"2026-08-17","sections":[{"capability":"fleet","title":"Fleet","summaryLabel":"Active vessels","count":4,"link":"/fleet","items":[{"title":"MV Aurora","detail":"En Route","dueLabel":null,"severity":"normal"}],"isUnavailable":false},{"capability":"crew","title":"Crew","summaryLabel":"Expiring credentials","count":0,"link":"/crew","items":[],"isUnavailable":false}],"priorityItems":[]}
            """);
        var page = Render<Home>();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".workspace-tab").Count));

        page.Find("input[type=search]").Input("  aurora  ");
        Assert.Single(page.FindAll(".workspace-tab"));
        Assert.Contains("Fleet", page.Find("#module-preview").TextContent);
        Assert.Equal(2, page.FindAll(".dashboard-summary-card").Count);

        page.Find("input[type=search]").Input("missing vessel");
        Assert.Empty(page.FindAll(".workspace-tab"));
        page.Find(".dashboard-modules .dashboard-empty button").Click();
        Assert.Equal(2, page.FindAll(".workspace-tab").Count);
    }

    [Fact]
    public void Home_workspace_switches_modules_with_mouse_and_keyboard()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        ConfigureClients("""
            {"asOf":"2026-08-17","sections":[{"capability":"fleet","title":"Fleet","summaryLabel":"Active vessels","count":1,"link":"/fleet","items":[{"title":"MV Aurora","detail":"En Route","dueLabel":null,"severity":"normal","link":"/fleet/aurora"}],"isUnavailable":false},{"capability":"voyages","title":"Voyages","summaryLabel":"Upcoming voyages","count":1,"link":"/voyages","items":[{"title":"VOY-01","detail":"LON to AMS · Planned","dueLabel":"2026-08-20","severity":"normal"}],"isUnavailable":false}],"priorityItems":[]}
            """);
        var page = Render<Home>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[role=tabpanel]")));
        Assert.Contains("En Route", page.Find(".workspace-fleet").TextContent);
        Assert.Equal("/fleet/aurora", page.Find(".workspace-fleet a").GetAttribute("href"));

        page.Find("#module-tab-voyages").Click();
        Assert.Empty(page.FindAll(".workspace-fleet"));
        Assert.Contains("LON → AMS", page.Find(".workspace-voyages").TextContent);
        Assert.Equal("true", page.Find("#module-tab-voyages").GetAttribute("aria-selected"));

        page.Find("#module-tab-voyages").KeyDown("ArrowRight");
        Assert.Equal("true", page.Find("#module-tab-fleet").GetAttribute("aria-selected"));
        page.Find("#module-tab-fleet").KeyDown("End");
        Assert.Equal("0", page.Find("#module-tab-voyages").GetAttribute("tabindex"));
    }

    [Fact]
    public void Home_priority_filter_shows_empty_feedback_and_returns_to_all_items()
    {
        ConfigureClients("""
            {"asOf":"2026-08-17","sections":[{"capability":"fleet","title":"Fleet","summaryLabel":"Active vessels","count":1,"link":"/fleet","items":[],"isUnavailable":false}],"priorityItems":[{"capability":"fleet","capabilityTitle":"Fleet","title":"MV Aurora","detail":"En Route","dueLabel":null,"severity":"normal","link":"/fleet"}]}
            """);
        var page = Render<Home>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".dashboard-priority-item")));

        page.FindAll(".dashboard-queue-filters button")[1].Click();
        Assert.Empty(page.FindAll(".dashboard-priority-item"));
        Assert.Contains("No items match this filter", page.Find(".dashboard-priority-list").TextContent);
        Assert.Equal("true", page.FindAll(".dashboard-queue-filters button")[1].GetAttribute("aria-pressed"));

        page.FindAll(".dashboard-queue-filters button")[0].Click();
        Assert.Single(page.FindAll(".dashboard-priority-item"));
    }

    [Fact]
    public void Home_agenda_orders_dates_and_excludes_overdue_and_out_of_window_items()
    {
        ConfigureClients("""
            {"asOf":"2026-08-17","sections":[{"capability":"documents","title":"Documents","summaryLabel":"Expiring documents","count":5,"link":"/documents","items":[{"title":"Later","detail":"Certificate","dueLabel":"2026-09-16","severity":"warning","link":"/documents/later"},{"title":"Past","detail":"Certificate","dueLabel":"2026-08-16","severity":"critical"},{"title":"Outside","detail":"Certificate","dueLabel":"2026-09-17","severity":"warning"},{"title":"Today","detail":"Certificate","dueLabel":"2026-08-17","severity":"warning","link":"/documents/today"},{"title":"Undated","detail":"Certificate","dueLabel":null,"severity":"normal"}],"isUnavailable":false}],"priorityItems":[]}
            """);
        var page = Render<Home>();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".dashboard-agenda-item").Count));

        var items = page.FindAll(".dashboard-agenda-item");
        Assert.Equal("/documents/today", items[0].GetAttribute("href"));
        Assert.Equal("/documents/later", items[1].GetAttribute("href"));
        Assert.Contains("Expires today", items[0].TextContent);
        Assert.Contains("1 / 1 modules loaded", page.Find(".dashboard-coverage").TextContent);
    }

    private void ConfigureClients(string dashboardJson)
    {
        var handler = new DashboardHandler(dashboardJson);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.harborops.test/") };
        Services.AddSingleton(new OperationsDashboardApiClient(client));
        Services.AddSingleton(new HealthApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.harborops.test/") },
            NullLogger<HealthApiClient>.Instance));
    }

    private sealed class DashboardHandler(string dashboardJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri!.AbsolutePath == "/health"
                ? new StringContent(string.Empty)
                : new StringContent(dashboardJson, Encoding.UTF8, "application/json");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
