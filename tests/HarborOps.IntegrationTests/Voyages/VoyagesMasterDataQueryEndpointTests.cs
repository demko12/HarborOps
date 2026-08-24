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

public sealed class VoyagesMasterDataQueryEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public VoyagesMasterDataQueryEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_ports_returns_the_searched_directory_page()
    {
        await SeedMasterDataAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        var response = await client.GetFromJsonAsync<SearchPortsResponse>(
            new Uri("/api/voyages/ports?search=Kotor", UriKind.Relative),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal("Port of Kotor", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Get_ports_with_an_oversized_page_size_returns_400()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.GetAsync(
            new Uri("/api/voyages/ports?pageSize=1000", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_berths_returns_the_berths_of_the_requested_port()
    {
        var seeded = await SeedMasterDataAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        var response = await client.GetFromJsonAsync<SearchBerthsResponse>(
            new Uri($"/api/voyages/berths?portId={seeded.BarId}", UriKind.Relative),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("Berth 1", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Get_berths_without_a_port_returns_400()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.GetAsync(
            new Uri("/api/voyages/berths", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_routes_returns_the_route_summaries()
    {
        await SeedMasterDataAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        var response = await client.GetFromJsonAsync<SearchRoutesResponse>(
            new Uri("/api/voyages/routes", UriKind.Relative),
            CancellationToken.None);

        Assert.NotNull(response);
        var route = Assert.Single(response.Items);
        Assert.Equal("Adriatic feeder", route.Name);
        Assert.Equal(2, route.StopCount);
    }

    [Fact]
    public async Task Get_route_details_returns_the_ordered_legs()
    {
        var seeded = await SeedMasterDataAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        var response = await client.GetFromJsonAsync<RouteDetailsResponse>(
            new Uri($"/api/voyages/routes/{seeded.RouteId}", UriKind.Relative),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal([1, 2], response.Stops.Select(stop => stop.StopOrder));
        Assert.Equal(
            ["Port of Bar", "Port of Kotor"],
            response.Stops.Select(stop => stop.PortName));
        Assert.Equal([0m, 60m], response.Stops.Select(stop => stop.DistanceFromPrevious));
    }

    [Fact]
    public async Task Get_route_details_for_an_unknown_route_returns_404()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.GetAsync(
            new Uri($"/api/voyages/routes/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Lookups_return_bounded_selector_items()
    {
        var seeded = await SeedMasterDataAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        var ports = await client.GetFromJsonAsync<PortLookupResponse>(
            new Uri("/api/voyages/ports/lookup?take=1", UriKind.Relative),
            CancellationToken.None);
        var berths = await client.GetFromJsonAsync<BerthLookupResponse>(
            new Uri($"/api/voyages/berths/lookup?portId={seeded.BarId}", UriKind.Relative),
            CancellationToken.None);
        var routes = await client.GetFromJsonAsync<RouteLookupResponse>(
            new Uri("/api/voyages/routes/lookup?search=Adriatic", UriKind.Relative),
            CancellationToken.None);

        Assert.NotNull(ports);
        Assert.NotNull(berths);
        Assert.NotNull(routes);
        Assert.Equal("Port of Bar", Assert.Single(ports.Items).Name);
        Assert.Equal("Berth 1", Assert.Single(berths.Items).Name);
        Assert.Equal("Adriatic feeder", Assert.Single(routes.Items).Name);
    }

    [Fact]
    public async Task Get_ports_lookup_with_an_oversized_take_returns_400()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.GetAsync(
            new Uri("/api/voyages/ports/lookup?take=500", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/voyages/ports")]
    [InlineData("/api/voyages/ports/lookup")]
    [InlineData("/api/voyages/berths/lookup")]
    [InlineData("/api/voyages/routes")]
    [InlineData("/api/voyages/routes/lookup")]
    public async Task Queries_without_authentication_return_401(string path)
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/voyages/ports")]
    [InlineData("/api/voyages/ports/lookup")]
    [InlineData("/api/voyages/berths/lookup")]
    [InlineData("/api/voyages/routes")]
    [InlineData("/api/voyages/routes/lookup")]
    public async Task Queries_without_the_read_permission_return_403(string path)
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Route_details_without_the_read_permission_return_403()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync(
            new Uri($"/api/voyages/routes/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<SeededMasterData> SeedMasterDataAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var bar = Port.Create("Port of Bar", "Montenegro", "Bar", "MEBAR");
        var kotor = Port.Create("Port of Kotor", "Montenegro", "Kotor", "MEKOT");
        var route = Route.Create("Adriatic feeder", 60m, 300, "Bar to Kotor");

        await dbContext.Set<Port>().AddRangeAsync([bar, kotor], CancellationToken.None);
        await dbContext.Set<Berth>().AddAsync(
            Berth.Create(bar.Id, "Berth 1", 210m, 12m, "Available"),
            CancellationToken.None);
        await dbContext.Set<Route>().AddAsync(route, CancellationToken.None);
        await dbContext.Set<RoutePort>().AddRangeAsync(
            [
                RoutePort.Create(route.Id, kotor.Id, 2, 60m, 300),
                RoutePort.Create(route.Id, bar.Id, 1, 0m, 0),
            ],
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return new SeededMasterData(bar.Id, kotor.Id, route.Id);
    }

    private sealed record SeededMasterData(Guid BarId, Guid KotorId, Guid RouteId);
}
