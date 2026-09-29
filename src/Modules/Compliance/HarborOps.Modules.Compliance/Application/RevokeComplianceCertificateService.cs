using System.Data;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HarborOps.Modules.Compliance.Application;

public sealed class RevokeComplianceCertificateService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<RevokeComplianceCertificateResponse> RevokeAsync(
        Guid certificateId,
        Guid actorUserId,
        RevokeComplianceCertificateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RevokeComplianceCertificateValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RevokeComplianceCertificateValidationException(errors);
        }

        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);

        var certificate = await dbContext.Set<ComplianceCertificate>()
            .SingleOrDefaultAsync(candidate => candidate.Id == certificateId, cancellationToken)
            ?? throw new ComplianceCertificateNotFoundException(certificateId);

        if (certificate.IsRevoked)
        {
            throw new ComplianceCertificateConflictException("The certificate is already revoked.");
        }

        if (certificate.ConcurrencyToken != request.ExpectedConcurrencyToken)
        {
            throw new ComplianceCertificateConflictException(
                "The certificate was changed by another user. Reload it and try again.");
        }

        var reason = request.Reason!.Trim();
        var revokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        certificate.Revoke(actorUserId, reason, revokedAtUtc);

        await dbContext.Set<ComplianceCertificateHistory>().AddAsync(
            ComplianceCertificateHistory.RecordRevoked(certificate.Id, reason, actorUserId, revokedAtUtc),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return new RevokeComplianceCertificateResponse(certificate.Id, revokedAtUtc, actorUserId);
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
}
