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

public sealed class CloseNonConformityEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public CloseNonConformityEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_closes_an_in_progress_finding_and_persists_closure_fields()
    {
        var nonConformityId = await SeedNonConformityWithPriorActionAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var finalEvidenceDocumentId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/non-conformities/{nonConformityId}/close",
            new CloseNonConformityRequest("All actions verified.", finalEvidenceDocumentId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CloseNonConformityResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(NonConformity.ClosureStatusClosed, result.ClosureStatus);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persisted = await dbContext.Set<NonConformity>().AsNoTracking().SingleAsync(n => n.Id == nonConformityId);
        Assert.Equal(NonConformity.ClosureStatusClosed, persisted.ClosureStatus);
        Assert.Equal("All actions verified.", persisted.ClosureNotes);
        Assert.NotNull(persisted.ClosedAtUtc);
        Assert.NotNull(persisted.ClosedByUserId);
        Assert.Equal(finalEvidenceDocumentId, persisted.FinalEvidenceDocumentId);
    }

    [Fact]
    public async Task Post_returns_400_for_empty_closure_notes()
    {
        var nonConformityId = await SeedNonConformityWithPriorActionAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/non-conformities/{nonConformityId}/close",
            new CloseNonConformityRequest("", null),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(CloseNonConformityValidator.ClosureNotesField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_404_for_an_unknown_non_conformity()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/non-conformities/999999/close",
            new CloseNonConformityRequest("Notes.", null),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_409_for_an_open_finding_with_no_corrective_actions()
    {
        var nonConformityId = await SeedNonConformityAsync(NonConformity.ClosureStatusOpen);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/non-conformities/{nonConformityId}/close",
            new CloseNonConformityRequest("Notes.", null),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_409_for_an_already_closed_finding()
    {
        var nonConformityId = await SeedNonConformityAsync(NonConformity.ClosureStatusClosed);
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/non-conformities/{nonConformityId}/close",
            new CloseNonConformityRequest("Notes.", null),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_403_with_read_only_permission()
    {
        var nonConformityId = await SeedNonConformityWithPriorActionAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/non-conformities/{nonConformityId}/close",
            new CloseNonConformityRequest("Notes.", null),
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

    private async Task<int> SeedNonConformityWithPriorActionAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var nonConformity = new NonConformity(
            Guid.NewGuid(), NonConformity.SeverityMajor, new DateOnly(2026, 12, 1), NonConformity.ClosureStatusOpen);
        await dbContext.AddAsync(nonConformity);
        await dbContext.SaveChangesAsync();

        nonConformity.AddCorrectiveAction("Initial corrective action.", Guid.NewGuid(), DateTimeOffset.UtcNow, null);
        await dbContext.SaveChangesAsync();

        return nonConformity.Id;
    }
}
