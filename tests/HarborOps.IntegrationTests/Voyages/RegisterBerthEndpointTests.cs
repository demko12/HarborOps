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

public sealed class RegisterBerthEndpointTests : IntegrationTestBase
{
    private const string Path = "/api/voyages/berths";

    private readonly HarborOpsApiFactory _application;

    public RegisterBerthEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_registers_and_persists_the_berth()
    {
        var portId = await SeedPortAsync("Port of Bar");
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(portId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterBerthResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal($"/api/voyages/berths/{result.Id}", response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var berth = await dbContext.Set<Berth>()
            .SingleAsync(candidate => candidate.Id == result.Id, CancellationToken.None);

        Assert.Equal(portId, berth.PortId);
        Assert.Equal("Berth 1", berth.Name);
        Assert.Equal(210m, berth.Length);
        Assert.Equal(12m, berth.MaxDraft);
        Assert.Equal("Available", berth.Status);
    }

    [Fact]
    public async Task Post_for_an_unknown_port_returns_404()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(Guid.NewGuid()),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("portId", problem.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Post_with_an_existing_berth_name_in_the_port_returns_409()
    {
        var portId = await SeedPortAsync("Port of Bar");
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        using var created = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(portId),
            CancellationToken.None);
        created.EnsureSuccessStatusCode();

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(portId) with { Name = "berth 1" },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("name", problem.Errors.Keys, StringComparer.Ordinal);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        Assert.Equal(1, await dbContext.Set<Berth>().CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Post_with_a_non_positive_length_returns_400()
    {
        var portId = await SeedPortAsync("Port of Bar");
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(portId) with { Length = 0m },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("length", problem.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Post_with_a_status_outside_the_allowed_list_returns_400()
    {
        var portId = await SeedPortAsync("Port of Bar");
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(portId) with { Status = "Operational" },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("status", problem.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(Guid.NewGuid()),
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
            CreateValidRequest(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static RegisterBerthRequest CreateValidRequest(Guid portId)
    {
        return new RegisterBerthRequest
        {
            PortId = portId,
            Name = "Berth 1",
            Length = 210m,
            MaxDraft = 12m,
            Status = "Available",
        };
    }

    private async Task<Guid> SeedPortAsync(string name)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var port = Port.Create(name, "Montenegro");

        await dbContext.Set<Port>().AddAsync(port, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return port.Id;
    }
}
