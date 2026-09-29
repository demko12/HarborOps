using System.Data;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HarborOps.Modules.Compliance.Application;

public sealed class RenewComplianceCertificateService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<RenewComplianceCertificateResponse> RenewAsync(
        Guid certificateId,
        Guid actorUserId,
        RenewComplianceCertificateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RenewComplianceCertificateValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RenewComplianceCertificateValidationException(errors);
        }

        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);

        var certificates = dbContext.Set<ComplianceCertificate>();
        var predecessor = await certificates
            .SingleOrDefaultAsync(candidate => candidate.Id == certificateId, cancellationToken)
            ?? throw new ComplianceCertificateNotFoundException(certificateId);

        if (predecessor.IsRevoked)
        {
            throw new ComplianceCertificateConflictException(
                "A revoked certificate cannot be renewed. Register a new certificate instead.");
        }

        var hasSuccessor = await certificates
            .AnyAsync(candidate => candidate.PredecessorCertificateId == predecessor.Id, cancellationToken);
        if (hasSuccessor)
        {
            throw new ComplianceCertificateConflictException(
                "This certificate has already been renewed. Renew the latest certificate in the chain.");
        }

        if (predecessor.ConcurrencyToken != request.ExpectedConcurrencyToken)
        {
            throw new ComplianceCertificateConflictException(
                "The certificate was changed by another user. Reload it and try again.");
        }

        var dateErrors = RenewComplianceCertificateValidator.ValidateAgainstPredecessor(request, predecessor);
        if (dateErrors.Count > 0)
        {
            throw new RenewComplianceCertificateValidationException(dateErrors);
        }

        var renewedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var successor = predecessor.Renew(
            request.CertificateNumber!.Trim(),
            request.IssuingAuthority!.Trim(),
            request.IssuedOn!.Value,
            request.ExpiresOn!.Value,
            renewedAtUtc);

        await certificates.AddAsync(successor, cancellationToken);
        await dbContext.Set<ComplianceCertificateHistory>().AddAsync(
            ComplianceCertificateHistory.RecordRenewed(predecessor.Id, successor.Id, actorUserId, renewedAtUtc),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return new RenewComplianceCertificateResponse(successor.Id, predecessor.Id);
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
}
