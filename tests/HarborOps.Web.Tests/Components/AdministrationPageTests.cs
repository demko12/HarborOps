using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Components.Pages.Administration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class AdministrationPageTests : BunitContext
{
    private readonly List<string> mutations = [];

    [Fact]
    public void Closing_user_editor_discards_unsaved_roles_and_opens_the_role_editor_independently()
    {
        var page = RenderPage();
        Assert.Equal(2, page.FindAll(".administration-grid > section").Count);
        Assert.DoesNotContain("Select a user or role", page.Markup);
        page.Find(".user-row").Click();
        Assert.Equal("User roles", page.Find("dialog h2").TextContent);
        page.Find("dialog .choice input").Change(false);
        page.Find("[aria-label='Close access editor']").Click();
        Assert.Empty(page.FindAll("dialog h2"));
        page.Find(".user-row").Click();
        Assert.True(page.Find("dialog .choice input").HasAttribute("checked"));
        page.Find("[aria-label='Close access editor']").Click();
        page.Find(".role-row").Click();
        Assert.Equal("Role permissions", page.Find("dialog h2").TextContent);
        Assert.NotEmpty(page.FindAll("dialog .permission-groups fieldset"));
        Assert.Empty(mutations);
    }

    [Fact]
    public void User_role_replacement_still_requires_confirmation_in_the_dialog()
    {
        var page = RenderPage();
        page.Find(".user-row").Click();
        page.FindAll("dialog button").Single(button => button.TextContent == "Replace roles").Click();
        Assert.Empty(mutations);
        page.FindAll("dialog button").Single(button => button.TextContent == "Confirm replacement").Click();
        page.WaitForAssertion(() => Assert.Single(mutations));
        Assert.EndsWith("/users/10000000-0000-0000-0000-000000000001/roles", mutations[0]);
        Assert.Contains("Roles replaced successfully.", page.Find("dialog").TextContent);
    }

    private IRenderedComponent<Administration> RenderPage()
    {
        JSInterop.SetupModule("./js/administration-dialog.js").Mode = JSRuntimeMode.Loose;
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType, "identity.users.manage"));
        identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType, "identity.roles.manage"));
        Services.AddSingleton(new CurrentUserAuthorizationState(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        }));
        Services.AddMemoryCache();
        Services.AddSingleton<ServerSessionTicketStore>();
        Services.AddSingleton(new IdentityAdministrationApiClient(new HttpClient(new Handler(mutations))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        }));
        return Render<Administration>();
    }

    private sealed class Handler(List<string> mutations) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get)
            {
                mutations.Add(request.RequestUri!.AbsolutePath);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            var json = request.RequestUri!.AbsolutePath.EndsWith("/roles", StringComparison.Ordinal)
                ? """[{"id":"20000000-0000-0000-0000-000000000002","name":"Operator","permissions":["fleet.read"]}]"""
                : """{"items":[{"id":"10000000-0000-0000-0000-000000000001","email":"operator@harborops.local","roles":["Operator"],"isActive":true}],"pageNumber":1,"pageSize":10,"totalCount":1}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
