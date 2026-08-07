using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class RegisterComplianceCertificateEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RegisterComplianceCertificateEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_registers_and_persists_certificate()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/certificates",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterComplianceCertificateResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(
            $"/api/compliance/certificates/{result.Id}",
            response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var certificate = await dbContext
            .Set<ComplianceCertificate>()
            .SingleAsync(
                candidate => candidate.Id == result.Id,
                CancellationToken.None);
        var entityType = dbContext.Model.FindEntityType(typeof(ComplianceCertificate));

        Assert.Equal(request.VesselId, certificate.VesselId);
        Assert.Equal(request.CertificateType, certificate.CertificateType);
        Assert.Equal(request.CertificateNumber, certificate.CertificateNumber);
        Assert.Equal(request.IssuingAuthority, certificate.IssuingAuthority);
        Assert.Equal(request.IssuedOn, certificate.IssuedOn);
        Assert.Equal(request.ExpiresOn, certificate.ExpiresOn);
        Assert.Equal("ComplianceCertificates", entityType!.GetTableName());
        Assert.Equal(ComplianceModule.Schema, entityType!.GetSchema());
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = _application.CreateClient();
        var request = new RegisterComplianceCertificateRequest
        {
            VesselId = Guid.Empty,
            CertificateType = string.Empty,
            CertificateNumber = "CN-1",
            IssuingAuthority = "Flag State Administration",
            IssuedOn = new DateOnly(2026, 6, 1),
            ExpiresOn = new DateOnly(2026, 5, 1),
        };

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/certificates",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterComplianceCertificateValidator.VesselIdField, problem.Errors);
        Assert.Contains(RegisterComplianceCertificateValidator.CertificateTypeField, problem.Errors);
        Assert.Contains(RegisterComplianceCertificateValidator.ExpiresOnField, problem.Errors);
    }

    [Fact]
    public void ComplianceModule_registers_required_application_service()
    {
        // The POST tests above already prove the controller resolves correctly via DI
        // (a routed request would fail if ComplianceCertificatesController could not be
        // activated). This test isolates the application-service registration itself.
        using var scope = _application.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<RegisterComplianceCertificateService>();

        Assert.NotNull(service);
    }

    private static RegisterComplianceCertificateRequest CreateValidRequest()
    {
        return new RegisterComplianceCertificateRequest
        {
            VesselId = Guid.NewGuid(),
            CertificateType = "Safety Management Certificate",
            CertificateNumber = "SMC-2026-002",
            IssuingAuthority = "Flag State Administration",
            IssuedOn = new DateOnly(2026, 1, 1),
            ExpiresOn = new DateOnly(2027, 1, 1),
        };
    }
}
