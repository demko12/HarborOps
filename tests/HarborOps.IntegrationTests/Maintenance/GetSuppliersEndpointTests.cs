using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class GetSuppliersEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetSuppliersEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_a_correctly_shaped_page()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync("Baltic Marine Parts", "Ana Ilic");

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/suppliers", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content
            .ReadFromJsonAsync<GetSuppliersResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(GetSuppliersValidator.DefaultPageSize, page.PageSize);
        Assert.Equal(1, page.TotalCount);

        var item = Assert.Single(page.Items);
        Assert.Equal(supplier.Id, item.SupplierId);
        Assert.Equal("Baltic Marine Parts", item.Name);
        Assert.Equal("Ana Ilic", item.ContactPerson);
    }

    [Fact]
    public async Task Get_filters_by_search()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var match = await SeedSupplierAsync("Nordic Filters", null);
        await SeedSupplierAsync("Aegean Trading", null);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/suppliers?search=Nordic", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content
            .ReadFromJsonAsync<GetSuppliersResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(match.Id, Assert.Single(page.Items).SupplierId);
    }

    [Fact]
    public async Task Get_returns_400_for_an_invalid_page_size()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri(
                $"/api/maintenance/suppliers?pageSize={GetSuppliersValidator.MaxPageSize + 1}",
                UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_a_maintenance_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientAsync(roleNames: []);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/suppliers", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Supplier> SeedSupplierAsync(string name, string? contactPerson)
    {
        var supplier = Supplier.Register(name, contactPerson, null, null, null);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return supplier;
    }
}
