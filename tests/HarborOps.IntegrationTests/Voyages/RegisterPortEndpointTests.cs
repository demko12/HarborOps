using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class RegisterPortEndpointTests : IntegrationTestBase
{
    private const string Path = "/api/voyages/ports";

    private readonly HarborOpsApiFactory _application;

    public RegisterPortEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_registers_and_persists_the_port()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterPortResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal($"/api/voyages/ports/{result.Id}", response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var port = await dbContext.Set<Port>()
            .SingleAsync(candidate => candidate.Id == result.Id, CancellationToken.None);

        Assert.Equal("Port of Bar", port.Name);
        Assert.Equal("Montenegro", port.Country);
        Assert.Equal("MEBAR", port.UnLocode);
    }

    [Fact]
    public async Task Post_without_a_name_returns_400()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest() with { Name = null },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("name", problem.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Post_with_an_existing_un_locode_returns_409()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        using var created = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(),
            CancellationToken.None);
        created.EnsureSuccessStatusCode();

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest() with { Name = "Bar terminal", UnLocode = "mebar" },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("unLocode", problem.Errors.Keys, StringComparer.Ordinal);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        Assert.Equal(1, await dbContext.Set<Port>().CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_only_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static RegisterPortRequest CreateValidRequest()
    {
        return new RegisterPortRequest
        {
            Name = "Port of Bar",
            Country = "Montenegro",
            City = "Bar",
            UnLocode = "MEBAR",
            Latitude = 42.0936m,
            Longitude = 19.0894m,
        };
    }
}
