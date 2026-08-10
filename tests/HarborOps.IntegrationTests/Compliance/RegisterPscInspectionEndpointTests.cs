using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class RegisterPscInspectionEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public RegisterPscInspectionEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_registers_and_persists_inspection()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/psc-inspections",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterPscInspectionResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(
            $"/api/compliance/psc-inspections/{result.Id}",
            response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var inspection = await dbContext
            .Set<PscInspection>()
            .SingleAsync(
                candidate => candidate.Id == result.Id,
                CancellationToken.None);
        var entityType = dbContext.Model.FindEntityType(typeof(PscInspection));

        Assert.Equal(request.VesselId, inspection.VesselId);
        Assert.Equal(request.PortId, inspection.PortId);
        Assert.Equal(request.InspectionDate, inspection.Date);
        Assert.Equal(request.InspectingAuthority, inspection.InspectingAuthority);
        Assert.Equal(request.DeficiencyCount, inspection.DeficiencyCount);
        Assert.Equal(request.VesselDetained, inspection.VesselDetained);
        Assert.Equal("PscInspections", entityType!.GetTableName());
        Assert.Equal(ComplianceModule.Schema, entityType!.GetSchema());
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var request = new RegisterPscInspectionRequest
        {
            VesselId = Guid.Empty,
            PortId = Guid.NewGuid(),
            InspectionDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            InspectingAuthority = "Port Authority",
            DeficiencyCount = 1,
            VesselDetained = false,
        };

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/psc-inspections",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterPscInspectionValidator.VesselIdField, problem.Errors);
        Assert.Contains(RegisterPscInspectionValidator.InspectionDateField, problem.Errors);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/psc-inspections",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void ComplianceModule_registers_required_application_service()
    {

        using var scope = _application.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<RegisterPscInspectionService>();

        Assert.NotNull(service);
    }

    private static RegisterPscInspectionRequest CreateValidRequest()
    {
        return new RegisterPscInspectionRequest
        {
            VesselId = Guid.NewGuid(),
            PortId = Guid.NewGuid(),
            InspectionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            InspectingAuthority = "Port Authority",
            DeficiencyCount = 3,
            VesselDetained = true,
        };
    }
}
