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

public sealed class GetNonConformitiesEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public GetNonConformitiesEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_filtered_and_paged_results()
    {
        var pscId = await SeedPscInspectionAsync();
        var deadline = new DateOnly(2026, 12, 1);
        await SeedNonConformityAsync(NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, deadline, pscId);
        await SeedNonConformityAsync(NonConformity.SeverityMinor, NonConformity.ClosureStatusOpen, deadline, pscId);
        var expectedMajor = await SeedNonConformityAsync(NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, deadline.AddDays(1), pscId);

        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);
        using var response = await client.GetAsync(
            $"/api/compliance/non-conformities?severity={NonConformity.SeverityMajor}&pageNumber=2&pageSize=1");
        var result = await response.Content.ReadFromJsonAsync<GetNonConformitiesResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(expectedMajor.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task Get_returns_400_for_an_invalid_page_size()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);
        using var response = await client.GetAsync("/api/compliance/non-conformities?pageSize=0");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetNonConformitiesValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_400_for_an_unknown_severity_value()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);
        using var response = await client.GetAsync("/api/compliance/non-conformities?severity=Severe");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetNonConformitiesValidator.SeverityField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_403_without_compliance_permissions()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);
        using var response = await client.GetAsync("/api/compliance/non-conformities");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<int> SeedPscInspectionAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var inspection = new PscInspection(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), "Port Authority", 1, false);
        await dbContext.AddAsync(inspection);
        await dbContext.SaveChangesAsync();
        return inspection.Id;
    }

    private async Task<NonConformity> SeedNonConformityAsync(string severity, string closureStatus, DateOnly deadline, int pscId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var nonConformity = new NonConformity(Guid.NewGuid(), severity, deadline, closureStatus, pscId: pscId);
        await dbContext.AddAsync(nonConformity);
        await dbContext.SaveChangesAsync();
        return nonConformity;
    }
}
