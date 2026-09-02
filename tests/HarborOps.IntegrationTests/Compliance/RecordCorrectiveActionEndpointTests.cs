using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class RecordCorrectiveActionEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public RecordCorrectiveActionEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_records_the_action_and_persists_it()
    {
        var nonConformityId = await SeedNonConformityAsync(NonConformity.ClosureStatusOpen);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/non-conformities/{nonConformityId}/corrective-actions",
            new RecordCorrectiveActionRequest("Replaced the seal.", null),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RecordCorrectiveActionResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(
            $"/api/compliance/non-conformities/{nonConformityId}",
            response.Headers.Location?.OriginalString);
        Assert.Equal(NonConformity.ClosureStatusInProgress, result.ClosureStatus);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persistedAction = await dbContext.Set<CorrectiveAction>()
            .SingleAsync(action => action.NonConformityId == nonConformityId);
        Assert.Equal("Replaced the seal.", persistedAction.ActionText);
    }

    [Fact]
    public async Task Post_returns_400_for_empty_action_text()
    {
        var nonConformityId = await SeedNonConformityAsync(NonConformity.ClosureStatusOpen);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/non-conformities/{nonConformityId}/corrective-actions",
            new RecordCorrectiveActionRequest("", null),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RecordCorrectiveActionValidator.ActionTextField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_404_for_an_unknown_non_conformity()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/non-conformities/999999/corrective-actions",
            new RecordCorrectiveActionRequest("Action.", null),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_409_for_an_already_closed_finding()
    {
        var nonConformityId = await SeedNonConformityAsync(NonConformity.ClosureStatusClosed);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/non-conformities/{nonConformityId}/corrective-actions",
            new RecordCorrectiveActionRequest("Too late.", null),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_403_with_read_only_permission()
    {
        var nonConformityId = await SeedNonConformityAsync(NonConformity.ClosureStatusOpen);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/non-conformities/{nonConformityId}/corrective-actions",
            new RecordCorrectiveActionRequest("Action.", null),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<int> SeedNonConformityAsync(string closureStatus)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var nonConformity = new NonConformity(
            Guid.NewGuid(), NonConformity.SeverityMajor, new DateOnly(2026, 12, 1), closureStatus);
        await dbContext.AddAsync(nonConformity);
        await dbContext.SaveChangesAsync();
        return nonConformity.Id;
    }
}
