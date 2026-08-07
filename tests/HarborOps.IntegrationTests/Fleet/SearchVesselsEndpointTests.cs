using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using FleetEntity = HarborOps.Modules.Fleet.Domain.Fleet;

namespace HarborOps.IntegrationTests.Fleet;

public sealed class SearchVesselsEndpointTests : IntegrationTestBase
{
    private static readonly string[] ExpectedItemProperties =
    [
        "currentStatus",
        "flagState",
        "fleetId",
        "fleetName",
        "id",
        "imoNumber",
        "lifecycleStage",
        "name",
        "vesselTypeId",
        "vesselTypeName",
    ];

    private readonly HarborOpsApiFactory _application;

    public SearchVesselsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_correctly_shaped_page()
    {
        using var client = _application.CreateClient();
        var seeded = await SeedVesselAsync("Aurora Borealis", "5100001", "Arctic Fleet");

        using var response = await client.GetAsync(
            new Uri("/api/fleet/vessels?search=5100001", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<SearchVesselsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(SearchVesselsValidator.DefaultPageSize, page.PageSize);
        Assert.Equal(1, page.TotalCount);

        var item = Assert.Single(page.Items);

        Assert.Equal(seeded.VesselId, item.Id);
        Assert.Equal("Aurora Borealis", item.Name);
        Assert.Equal("5100001", item.ImoNumber);
        Assert.Equal("Malta", item.FlagState);
        Assert.Equal("Active", item.CurrentStatus);
        Assert.Null(item.LifecycleStage);
        Assert.Equal(seeded.VesselTypeId, item.VesselTypeId);
        Assert.Equal("Container Ship", item.VesselTypeName);
        Assert.Equal(seeded.FleetId, item.FleetId);
        Assert.Equal("Arctic Fleet", item.FleetName);
    }

    [Fact]
    public async Task Get_returns_empty_items_when_nothing_matches()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/fleet/vessels?search=no-vessel-matches-this", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<SearchVesselsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(SearchVesselsValidator.DefaultPageSize, page.PageSize);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_page_size_above_maximum()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/fleet/vessels?pageSize=101", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(SearchVesselsValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_page_number_below_one()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/fleet/vessels?pageNumber=0", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(SearchVesselsValidator.PageNumberField, problem.Errors);
    }

    [Fact]
    public async Task Get_does_not_serialize_domain_entities()
    {
        using var client = _application.CreateClient();
        await SeedVesselAsync("Northern Light", "5100002", "Baltic Fleet");

        using var response = await client.GetAsync(
            new Uri("/api/fleet/vessels?search=5100002", UriKind.Relative),
            CancellationToken.None);
        var payload = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(payload);

        var item = document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Single();
        var itemProperties = item
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExpectedItemProperties, itemProperties);
        Assert.DoesNotContain("companyId", itemProperties, StringComparer.Ordinal);
        Assert.DoesNotContain("createdAtUtc", itemProperties, StringComparer.Ordinal);
        Assert.DoesNotContain("updatedAtUtc", itemProperties, StringComparer.Ordinal);
        Assert.DoesNotContain("mmsi", itemProperties, StringComparer.Ordinal);
    }

    private async Task<SeededVessel> SeedVesselAsync(
        string name,
        string imoNumber,
        string fleetName)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var vesselType = VesselType.Register(
            "Container Ship",
            "CNTR",
            "Container carrying vessel");
        var vessel = Vessel.Register(
            Guid.NewGuid(),
            vesselType.Id,
            name,
            imoNumber,
            "Malta",
            "Active");
        var fleet = FleetEntity.Register(fleetName, "Fleet used by search endpoint tests.");
        var assignment = FleetAssignment.Register(vessel.Id, fleet.Id);

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.Set<FleetEntity>().AddAsync(fleet);
        await dbContext.Set<FleetAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();

        return new SeededVessel(vessel.Id, vesselType.Id, fleet.Id);
    }

    private sealed record SeededVessel(Guid VesselId, Guid VesselTypeId, Guid FleetId);
}
