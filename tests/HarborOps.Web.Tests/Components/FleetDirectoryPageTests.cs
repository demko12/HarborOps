using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Fleet;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class FleetDirectoryPageTests : BunitContext
{
    private readonly List<Uri> requests = [];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Lists_are_default_and_view_switching_preserves_the_data_and_search(bool manage)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType,
            manage ? CurrentUserAuthorizationState.FleetManagePermission : CurrentUserAuthorizationState.FleetReadPermission));
        Services.AddSingleton(new CurrentUserAuthorizationState(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        }));
        Services.AddSingleton(new FleetApiClient(new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"items":[{"fleetId":"70000000-0000-0000-0000-000000000001","name":"Atlantic Fleet","description":"Regional operations","createdAtUtc":"2026-09-01T10:00:00Z","activeVesselCount":3}],"totalCount":1,"pageNumber":1,"pageSize":25}
                    """, Encoding.UTF8, "application/json"),
            });
        }))
        { BaseAddress = new Uri("https://api.harborops.test/") }));
        var page = Render<FleetDirectory>();

        Assert.Equal(manage, page.FindAll(".fleet-directory-create").Count == 1);
        Assert.Equal(!manage, page.Find(".fleet-directory-workspace").ClassList.Contains("fleet-directory-readonly"));
        if (manage) Assert.Equal("TEXTAREA", page.Find("#fleet-description").TagName);
        Assert.Equal("true", page.Find("button[aria-label='List view']").GetAttribute("aria-pressed"));
        page.Find("#fleet-directory-search").Change("Atlantic");
        page.WaitForAssertion(() => Assert.Contains("search=Atlantic", requests[^1].Query, StringComparison.Ordinal));
        var reads = requests.Count;
        page.Find("button[aria-label='Card view']").Click();
        Assert.Contains("fleet-directory-card-view", page.Find(".fleet-directory-results").ClassName);
        Assert.Equal("true", page.Find("button[aria-label='Card view']").GetAttribute("aria-pressed"));
        Assert.Contains("Atlantic Fleet", page.Find(".fleet-directory-card").TextContent, StringComparison.Ordinal);
        Assert.Equal("3", page.Find(".fleet-directory-vessels strong").TextContent);
        Assert.Equal("/fleet/fleets/70000000-0000-0000-0000-000000000001", page.Find(".fleet-directory-card").GetAttribute("href"));
        Assert.Equal("Atlantic", page.Find("#fleet-directory-search").GetAttribute("value"));
        page.Find("button[aria-label='List view']").Click();
        Assert.Contains("fleet-directory-list-view", page.Find(".fleet-directory-results").ClassName);
        Assert.Equal(reads, requests.Count);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
