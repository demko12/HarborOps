using System.Reflection;
using Bunit;
using HarborOps.Web.Components;
using HarborOps.Web.Components.Pages.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class SettingsPageTests : BunitContext
{
    [Fact]
    public void Settings_renders_light_dark_and_system_theme_options()
    {
        var page = RenderSettings("dark");

        Assert.Equal(3, page.FindAll(".theme-option").Count);
        Assert.Contains("Light", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Dark", page.Markup, StringComparison.Ordinal);
        Assert.Contains("System", page.Markup, StringComparison.Ordinal);
        Assert.Single(page.FindAll(".theme-option.selected"));
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("system")]
    public void Choosing_a_theme_persists_the_selected_local_preference(string preference)
    {
        var page = RenderSettings("dark");
        var option = page.FindAll(".theme-option")
            .Single(element => element.TextContent.Contains(
                char.ToUpperInvariant(preference[0]) + preference[1..],
                StringComparison.Ordinal));

        option.Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                "selected",
                page.FindAll(".theme-option")
                    .Single(element => element.TextContent.Contains(
                        char.ToUpperInvariant(preference[0]) + preference[1..],
                        StringComparison.Ordinal))
                    .ClassName,
                StringComparison.Ordinal);
            Assert.Contains(JSInterop.Invocations, invocation =>
                invocation.Identifier == "themeInterop.set"
                && string.Equals(invocation.Arguments[0]?.ToString(), preference, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Settings_route_requires_an_authenticated_user()
    {
        var authorization = typeof(Settings).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.True(string.IsNullOrEmpty(authorization.Policy));
    }

    private IRenderedComponent<Settings> RenderSettings(string preference)
    {
        JSInterop.Setup<string>("themeInterop.getPreference").SetResult(preference);
        JSInterop.SetupVoid("themeInterop.set", "light");
        JSInterop.SetupVoid("themeInterop.set", "dark");
        JSInterop.SetupVoid("themeInterop.set", "system");
        Services.AddScoped<ThemePreferenceState>();

        var page = Render<Settings>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".theme-option.selected")));

        return page;
    }
}
