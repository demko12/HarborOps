using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class RegisterSupplierEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RegisterSupplierEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_returns_201_and_persists_the_supplier()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/suppliers", UriKind.Relative),
            new
            {
                name = "Baltic Marine Parts",
                contactPerson = "Ana Ilic",
                phoneEmail = "sales@baltic.test",
                address = "Pier 4",
                paymentTerms = "Net 30",
            },
            CancellationToken.None);

        var result = await response.Content
            .ReadFromJsonAsync<RegisterSupplierResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.SupplierId);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var stored = await dbContext.Set<Supplier>().SingleAsync(CancellationToken.None);

        Assert.Equal(result.SupplierId, stored.Id);
        Assert.Equal("Baltic Marine Parts", stored.Name);
        Assert.Equal("Ana Ilic", stored.ContactPerson);
        Assert.Equal("Net 30", stored.PaymentTerms);
    }

    [Fact]
    public async Task Post_returns_400_without_a_name()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/suppliers", UriKind.Relative),
            new { contactPerson = "Ana Ilic" },
            CancellationToken.None);

        var problem = await response.Content
            .ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterSupplierValidator.NameField, problem.Errors);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/suppliers", UriKind.Relative),
            new { name = "Baltic Marine Parts" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_only_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/suppliers", UriKind.Relative),
            new { name = "Baltic Marine Parts" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
