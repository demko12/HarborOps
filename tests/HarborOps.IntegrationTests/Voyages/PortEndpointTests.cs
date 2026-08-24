using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class PortEndpointTests : IntegrationTestBase
{
    private static readonly Uri PortsUri = new("/api/voyages/ports", UriKind.Relative);
    private static readonly Uri LookupUri = new("/api/voyages/ports/lookup", UriKind.Relative);

    private readonly HarborOpsApiFactory _application;

    public PortEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_200_with_the_searchable_port_list()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);
        await SeedAsync(Port.Create("Rotterdam", "Netherlands", "Rotterdam", "NLRTM"));

        using var response = await client.GetAsync(
            new Uri("/api/voyages/ports?search=Rotter", UriKind.Relative),
            CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<SearchPortsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("Rotterdam", Assert.Single(payload.Items).Name);
    }

    [Fact]
    public async Task Get_returns_400_for_a_page_size_beyond_the_bound()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.GetAsync(
            new Uri("/api/voyages/ports?pageSize=101", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_requires_the_voyages_read_permission()
    {
        using var anonymous = _application.CreateClient();
        using var withoutPermission = await _application.CreateAuthenticatedClientAsync();

        using var anonymousResponse = await anonymous.GetAsync(PortsUri, CancellationToken.None);
        using var forbiddenResponse = await withoutPermission.GetAsync(PortsUri, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Fact]
    public async Task Post_lookup_returns_200_with_the_requested_port_names()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);
        var port = Port.Create("Hamburg", "Germany");
        await SeedAsync(port);

        using var response = await client.PostAsJsonAsync(
            LookupUri,
            new { portIds = new[] { port.Id } },
            CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<PortLookupResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);

        var item = Assert.Single(payload.Ports);

        Assert.Equal(port.Id, item.Id);
        Assert.Equal("Hamburg", item.Name);
    }

    [Fact]
    public async Task Post_lookup_returns_400_for_an_empty_request()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.PostAsJsonAsync(
            LookupUri,
            new { portIds = Array.Empty<Guid>() },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_lookup_rejects_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            LookupUri,
            new { portIds = new[] { Guid.NewGuid() } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_lookup_serves_a_planner_who_holds_no_voyages_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var port = Port.Create("Gdansk", "Poland", shipyardName: "Remontowa");
        await SeedAsync(port);

        using var response = await client.PostAsJsonAsync(
            LookupUri,
            new { portIds = new[] { port.Id } },
            CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<PortLookupResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("Gdansk", Assert.Single(payload.Ports).Name);
    }

    private async Task SeedAsync(params Port[] ports)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<Port>().AddRangeAsync(ports, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
