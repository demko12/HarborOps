using Bunit;
using HarborOps.Web.Components.Pages;

namespace HarborOps.Web.Tests.Components;

public sealed class NotFoundPageTests : BunitContext
{
    [Fact]
    public void The_page_explains_the_problem_under_a_single_top_level_heading()
    {
        var page = Render<NotFound>();

        var heading = page.Find("h1");

        Assert.Single(page.FindAll("h1"));
        Assert.Equal("Page not found", heading.TextContent);
        Assert.Equal(heading.Id, page.Find("section").GetAttribute("aria-labelledby"));
        Assert.Equal("status", page.Find(".not-found-card").GetAttribute("role"));
    }

    [Fact]
    public void Recovery_actions_offer_the_dashboard_and_the_previous_page()
    {
        var page = Render<NotFound>();

        var dashboard = page.Find(".not-found-actions a");
        var goBack = page.Find(".not-found-actions button");

        Assert.Equal("/", dashboard.GetAttribute("href"));
        Assert.Contains("Return to dashboard", dashboard.TextContent, StringComparison.Ordinal);
        Assert.Contains("Go back", goBack.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Going_back_returns_to_the_previous_browser_entry()
    {
        JSInterop.SetupVoid("history.back");

        var page = Render<NotFound>();

        page.Find(".not-found-actions button").Click();

        JSInterop.VerifyInvoke("history.back");
    }
}
