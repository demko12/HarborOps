using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class GetVesselComplianceOverviewEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetVesselComplianceOverviewEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_compliance_overview()
    {
        var vesselId = Guid.NewGuid();
        await using (var scope = _application.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
            await dbContext.AddAsync(ComplianceCertificate.Register(
                vesselId, "Safety", "CERT-1", "Authority",
                new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)));
            await dbContext.AddAsync(new PscInspection(
                vesselId, Guid.NewGuid(), new DateOnly(2026, 8, 1), "Port Authority", 1, false));
            await dbContext.SaveChangesAsync();
        }

        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);
        using var response = await client.GetAsync($"/api/compliance/vessels/{vesselId}/overview");
        var result = await response.Content.ReadFromJsonAsync<GetVesselComplianceOverviewResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Single(result.Certificates);
        Assert.Single(result.PscInspections);
        Assert.Empty(result.OpenNonConformities);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_empty_vessel_id()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);
        using var response = await client.GetAsync($"/api/compliance/vessels/{Guid.Empty}/overview");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetVesselComplianceOverviewValidator.VesselIdField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_empty_collections_when_vessel_has_no_compliance_data()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);
        using var response = await client.GetAsync($"/api/compliance/vessels/{Guid.NewGuid()}/overview");
        var result = await response.Content.ReadFromJsonAsync<GetVesselComplianceOverviewResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Empty(result.Certificates);
        Assert.Empty(result.PscInspections);
        Assert.Empty(result.OpenNonConformities);
    }
}
