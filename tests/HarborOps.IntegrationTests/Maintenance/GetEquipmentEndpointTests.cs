using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class GetEquipmentEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetEquipmentEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_200_with_a_correctly_shaped_page()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var equipment = await SeedEquipmentAsync(vesselId, "Main engine", "Propulsion", "Wartsila", "SN-1001");

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/equipment", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetEquipmentResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(GetEquipmentValidator.DefaultPageSize, page.PageSize);
        Assert.Equal(1, page.TotalCount);

        var item = Assert.Single(page.Items);
        Assert.Equal(equipment.Id, item.EquipmentId);
        Assert.Equal(vesselId, item.VesselId);
        Assert.Equal("Main engine", item.Name);
        Assert.Equal("Propulsion", item.Category);
        Assert.Equal("Wartsila", item.Manufacturer);
        Assert.Equal("SN-1001", item.SerialNumber);
    }

    [Fact]
    public async Task Get_filters_by_vessel_category_manufacturer_and_search()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        await SeedEquipmentAsync(vesselId, "Main engine", "Propulsion", "Wartsila", "SN-1001");
        await SeedEquipmentAsync(vesselId, "Boiler", "Auxiliary", "Alfa Laval", "SN-2002");
        await SeedEquipmentAsync(Guid.NewGuid(), "Other engine", "Propulsion", "Wartsila", "SN-3003");

        using var response = await client.GetAsync(
            new Uri(
                $"/api/maintenance/equipment?vesselId={vesselId}&category=Propulsion&manufacturer=Wartsila&search=SN-1001",
                UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetEquipmentResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal("Main engine", Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task Get_returns_400_for_an_invalid_page_size()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri(
                $"/api/maintenance/equipment?pageSize={GetEquipmentValidator.MaxPageSize + 1}",
                UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetEquipmentValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/equipment", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/equipment", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Equipment> SeedEquipmentAsync(
        Guid vesselId,
        string name,
        string category,
        string? manufacturer,
        string? serialNumber)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var equipment = Equipment.Register(vesselId, name, category, manufacturer, serialNumber);

        dbContext.Set<Equipment>().Add(equipment);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return equipment;
    }
}
