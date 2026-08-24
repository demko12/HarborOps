using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class PortNamesEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public PortNamesEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_names_returns_200_with_the_requested_port_names()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);
        var port = Port.Create("Hamburg", "Germany");
        await SeedAsync(port);

        using var response = await client.GetAsync(NamesUri(port.Id), CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<PortNamesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);

        var item = Assert.Single(payload.Ports);

        Assert.Equal(port.Id, item.PortId);
        Assert.Equal("Hamburg", item.Name);
        Assert.Equal("Germany", item.Country);
    }

    [Fact]
    public async Task Get_names_ignores_an_identifier_that_no_longer_exists()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);
        var port = Port.Create("Rotterdam", "Netherlands");
        await SeedAsync(port);

        using var response = await client.GetAsync(
            NamesUri(port.Id, Guid.NewGuid()),
            CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<PortNamesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("Rotterdam", Assert.Single(payload.Ports).Name);
    }

    [Fact]
    public async Task Get_names_returns_400_for_an_empty_request()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.GetAsync(NamesUri(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_names_rejects_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(NamesUri(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_names_requires_the_voyages_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(NamesUri(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Uri NamesUri(params Guid[] portIds) => new(
        "/api/voyages/ports/names?"
            + string.Join("&", portIds.Select(portId => $"portIds={portId:D}")),
        UriKind.Relative);

    private async Task SeedAsync(params Port[] ports)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<Port>().AddRangeAsync(ports, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
