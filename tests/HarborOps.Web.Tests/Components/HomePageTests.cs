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
            Assert.Equal(2, page.FindAll($"a[href='/crew/{crewMemberId}']").Count);
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
            Assert.Contains("This section could not be loaded", page.Markup, StringComparison.Ordinal);
        });
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
