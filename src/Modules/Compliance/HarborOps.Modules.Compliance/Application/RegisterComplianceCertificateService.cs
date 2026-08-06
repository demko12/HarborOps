using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

public sealed class RegisterComplianceCertificateService(DbContext dbContext)
{
    public async Task<RegisterComplianceCertificateResponse> RegisterAsync(
        RegisterComplianceCertificateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterComplianceCertificateValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterComplianceCertificateValidationException(errors);
        }

        var vesselId = request.VesselId!.Value;
        var certificateType = request.CertificateType!.Trim();
        var certificateNumber = request.CertificateNumber!.Trim();
        var issuingAuthority = request.IssuingAuthority!.Trim();
        var issuedOn = request.IssuedOn!.Value;
        var expiresOn = request.ExpiresOn!.Value;

        var certificate = ComplianceCertificate.Register(
            vesselId,
            certificateType,
            certificateNumber,
            issuingAuthority,
            issuedOn,
            expiresOn);

        var certificates = dbContext.Set<ComplianceCertificate>();

        await certificates.AddAsync(certificate, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterComplianceCertificateResponse(certificate.Id);
    }
}
