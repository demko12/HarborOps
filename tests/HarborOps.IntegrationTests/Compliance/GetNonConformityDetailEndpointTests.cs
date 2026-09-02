using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class GetNonConformityDetailEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public GetNonConformityDetailEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_the_finding_with_its_corrective_action_history()
    {
        int nonConformityId;
        await using (var scope = _application.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
            var inspection = new PscInspection(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), "Port Authority", 1, false);
            await dbContext.AddAsync(inspection);
            await dbContext.SaveChangesAsync();

            var nonConformity = new NonConformity(
                Guid.NewGuid(), NonConformity.SeverityMajor, new DateOnly(2026, 12, 1),
                NonConformity.ClosureStatusOpen, pscId: inspection.Id);
            await dbContext.AddAsync(nonConformity);
            await dbContext.SaveChangesAsync();

            nonConformity.AddCorrectiveAction("Investigated root cause.", Guid.NewGuid(), DateTimeOffset.UtcNow, null);
            await dbContext.SaveChangesAsync();

            nonConformityId = nonConformity.Id;
        }

        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);
        using var response = await client.GetAsync($"/api/compliance/non-conformities/{nonConformityId}");
        var result = await response.Content.ReadFromJsonAsync<GetNonConformityDetailResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nonConformityId, result.Id);
        Assert.Equal(NonConformity.ClosureStatusInProgress, result.ClosureStatus);
        Assert.Single(result.CorrectiveActions);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_id()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);
        using var response = await client.GetAsync("/api/compliance/non-conformities/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
