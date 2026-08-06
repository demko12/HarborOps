using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class RegisterComplianceCertificateServiceTests
{
    [Fact]
    public async Task Register_creates_certificate_and_trims_text()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterComplianceCertificateService(dbContext);
        var request = CreateValidRequest();
        var startedUtc = DateTime.UtcNow;

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var certificate = await dbContext
            .Set<ComplianceCertificate>()
            .SingleAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(response.Id, certificate.Id);
        Assert.Equal(request.VesselId, certificate.VesselId);
        Assert.Equal("Safety Management Certificate", certificate.CertificateType);
        Assert.Equal("SMC-2026-001", certificate.CertificateNumber);
        Assert.Equal("Flag State Administration", certificate.IssuingAuthority);
        Assert.Equal(request.IssuedOn, certificate.IssuedOn);
        Assert.Equal(request.ExpiresOn, certificate.ExpiresOn);
        Assert.InRange(certificate.CreatedUtc, startedUtc, DateTime.UtcNow);
    }

    [Fact]
    public async Task Register_rejects_empty_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterComplianceCertificateService(dbContext);
        var request = CreateValidRequest() with { VesselId = Guid.Empty };

        var exception = await Assert.ThrowsAsync<RegisterComplianceCertificateValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterComplianceCertificateValidator.VesselIdField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_expiresOn_earlier_than_issuedOn()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterComplianceCertificateService(dbContext);
        var request = CreateValidRequest() with
        {
            IssuedOn = new DateOnly(2026, 6, 1),
            ExpiresOn = new DateOnly(2026, 5, 1),
        };

        var exception = await Assert.ThrowsAsync<RegisterComplianceCertificateValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterComplianceCertificateValidator.ExpiresOnField, exception.Errors);
    }

    private static RegisterComplianceCertificateRequest CreateValidRequest()
    {
        return new RegisterComplianceCertificateRequest
        {
            VesselId = Guid.NewGuid(),
            CertificateType = " Safety Management Certificate ",
            CertificateNumber = " SMC-2026-001 ",
            IssuingAuthority = " Flag State Administration ",
            IssuedOn = new DateOnly(2026, 1, 1),
            ExpiresOn = new DateOnly(2027, 1, 1),
        };
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
