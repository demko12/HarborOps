using System.Net;
using System.Net.Http.Json;
using HarborOps.Api.Composition.Operations.Dashboard;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;

namespace HarborOps.IntegrationTests.Operations;

public sealed class OperationsDashboardEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory application;

    public OperationsDashboardEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        this.application = application;
    }

    [Fact]
    public async Task Get_requires_an_authenticated_user()
    {
        using var client = application.CreateClient();
        using var response = await client.GetAsync("/api/operations/dashboard?asOf=2026-08-17");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_requires_an_as_of_date()
    {
        using var client = await application.CreateAuthenticatedClientAsync();
        using var response = await client.GetAsync("/api/operations/dashboard");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_omits_sections_for_permissions_the_user_does_not_have()
    {
        using var client = await application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);
        using var response = await client.GetAsync("/api/operations/dashboard?asOf=2026-08-17");
        var dashboard = await response.Content.ReadFromJsonAsync<OperationsDashboardResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(dashboard);
        var section = Assert.Single(dashboard.Sections);
        Assert.Equal("fleet", section.Capability);
        Assert.InRange(section.Items.Count, 0, 10);
    }

    [Fact]
    public async Task Get_returns_an_empty_permission_aware_dashboard_when_no_module_read_permission_exists()
    {
        using var client = await application.CreateAuthenticatedClientAsync();
        using var response = await client.GetAsync("/api/operations/dashboard?asOf=2026-08-17");
        var dashboard = await response.Content.ReadFromJsonAsync<OperationsDashboardResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(dashboard);
        Assert.Empty(dashboard.Sections);
    }
}
