using System.Diagnostics;
using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using HarborOps.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class ErrorPageTests : BunitContext
{
    [Fact]
    public void The_page_states_the_failure_under_a_single_top_level_heading()
    {
        var page = RenderErrorPage();

        var heading = page.Find("h1");

        Assert.Single(page.FindAll("h1"));
        Assert.Equal("Something went wrong", heading.TextContent);
        Assert.Equal(heading.Id, page.Find("section").GetAttribute("aria-labelledby"));
        Assert.Equal("alert", page.Find(".error-card").GetAttribute("role"));
    }

    [Fact]
    public void Recovery_actions_offer_the_dashboard_and_a_retry()
    {
        var page = RenderErrorPage();

        var dashboard = page.Find(".error-actions a");
        var tryAgain = page.Find(".error-actions button");

        Assert.Equal("/", dashboard.GetAttribute("href"));
        Assert.Contains("Return to dashboard", dashboard.TextContent, StringComparison.Ordinal);
        Assert.Contains("Try again", tryAgain.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Trying_again_reloads_the_current_address()
    {
        var navigation = Services.GetRequiredService<BunitNavigationManager>();
        var page = RenderErrorPage();

        page.Find(".error-actions button").Click();

        var lastNavigation = Assert.Single(navigation.History);

        Assert.Equal(navigation.Uri, lastNavigation.Uri);
        Assert.True(lastNavigation.Options.ForceLoad);
    }

    [Fact]
    public void A_known_request_identifier_is_shown_as_a_support_reference()
    {
        var page = RenderErrorPage(new DefaultHttpContext { TraceIdentifier = "0HNF6QK4TR9AB:00000004" });

        var reference = page.Find(".error-reference");

        Assert.Contains("Support reference", reference.TextContent, StringComparison.Ordinal);
        Assert.Equal("0HNF6QK4TR9AB:00000004", reference.QuerySelector("code")!.TextContent);
        Assert.Contains("include the support reference above", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void No_reference_block_is_rendered_when_there_is_no_request_identifier()
    {
        var page = RenderErrorPage();

        Assert.Empty(page.FindAll(".error-reference"));
        Assert.Empty(page.FindAll("code"));
        Assert.DoesNotContain("Support reference", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_request_identifier_survives_the_switch_from_prerender_to_the_interactive_circuit()
    {
        var requestId = typeof(Error).GetProperty(
            "RequestId",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(requestId);
        Assert.NotNull(requestId.GetCustomAttribute<PersistentStateAttribute>());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("ASPNETCORE_ENVIRONMENT")]
    [InlineData("environment variable")]
    [InlineData("Exception")]
    [InlineData("stack")]
    [InlineData("sensitive information")]
    public void Development_guidance_and_exception_details_are_never_rendered(string forbidden)
    {
        var page = RenderErrorPage(new DefaultHttpContext { TraceIdentifier = "0HNF6QK4TR9AB:00000004" });

        Assert.DoesNotContain(forbidden, page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    private IRenderedComponent<Error> RenderErrorPage(HttpContext? httpContext = null)
    {
        // The page prefers the ambient activity id; clearing it keeps the test's own identifier authoritative.
        Activity.Current = null;

        return httpContext is null
            ? Render<Error>()
            : Render<Error>(parameters => parameters.AddCascadingValue(httpContext));
    }
}
