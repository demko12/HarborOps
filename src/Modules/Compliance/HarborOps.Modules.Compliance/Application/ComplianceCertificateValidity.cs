using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed record ComplianceCertificateSuccessorKey(Guid Id, Guid VesselId, string CertificateType);

internal sealed record ComplianceCertificateSuccessor(
    Guid Id,
    string CertificateNumber,
    DateOnly IssuedOn,
    DateOnly ExpiresOn,
    bool IsRevoked);

internal static class ComplianceCertificateValidity
{
    internal static string Status(DateOnly issuedOn, DateOnly expiresOn, bool isRevoked, DateOnly asOf)
    {
        if (isRevoked)
        {
            return ComplianceCertificate.RevokedStatus;
        }

        if (issuedOn > asOf)
        {
            return ComplianceCertificate.NotYetEffectiveStatus;
        }

        return expiresOn < asOf
            ? ComplianceCertificate.ExpiredStatus
            : ComplianceCertificate.CurrentStatus;
    }

    internal static bool CoversExpiry(
        DateOnly originalExpiresOn,
        DateOnly successorIssuedOn,
        DateOnly successorExpiresOn,
        bool successorIsRevoked)
    {
        return !successorIsRevoked
            && successorIssuedOn <= originalExpiresOn.AddDays(1)
            && successorExpiresOn > originalExpiresOn;
    }

    internal static int GapDays(DateOnly originalExpiresOn, DateOnly successorIssuedOn)
    {
        return Math.Max(0, successorIssuedOn.DayNumber - originalExpiresOn.DayNumber - 1);
    }

    internal static async Task<IReadOnlyDictionary<Guid, ComplianceCertificateSuccessor>> LoadSuccessorsAsync(
        DbContext dbContext,
        IReadOnlyCollection<ComplianceCertificateSuccessorKey> predecessors,
        CancellationToken cancellationToken)
    {
        if (predecessors.Count == 0)
        {
            return new Dictionary<Guid, ComplianceCertificateSuccessor>();
        }

        var predecessorIds = predecessors.Select(predecessor => predecessor.Id).Distinct().ToList();
        var keys = predecessors.ToDictionary(predecessor => predecessor.Id);

        var successors = await dbContext.Set<ComplianceCertificate>()
            .AsNoTracking()
            .Where(successor => successor.PredecessorCertificateId != null
                && predecessorIds.Contains(successor.PredecessorCertificateId.Value))
            .Select(successor => new
            {
                PredecessorCertificateId = successor.PredecessorCertificateId!.Value,
                successor.Id,
                successor.VesselId,
                successor.CertificateType,
                successor.CertificateNumber,
                successor.IssuedOn,
                successor.ExpiresOn,
                successor.RevokedAtUtc,
                successor.CreatedUtc,
            })
            .ToListAsync(cancellationToken);

        return successors
            .Where(successor => successor.Id != successor.PredecessorCertificateId
                && keys[successor.PredecessorCertificateId].VesselId == successor.VesselId
                && string.Equals(keys[successor.PredecessorCertificateId].CertificateType, successor.CertificateType, StringComparison.Ordinal))
            .GroupBy(successor => successor.PredecessorCertificateId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(successor => successor.CreatedUtc)
                    .ThenBy(successor => successor.Id)
                    .Select(successor => new ComplianceCertificateSuccessor(
                        successor.Id,
                        successor.CertificateNumber,
                        successor.IssuedOn,
                        successor.ExpiresOn,
                        successor.RevokedAtUtc is not null))
                    .First());
    }

    internal static IQueryable<ComplianceCertificate> WithoutCoveringSuccessor(
        IQueryable<ComplianceCertificate> certificates,
        IQueryable<ComplianceCertificate> allCertificates)
    {
        return certificates.Where(certificate => !allCertificates.Any(successor =>
            successor.PredecessorCertificateId == certificate.Id
            && successor.VesselId == certificate.VesselId
            && successor.CertificateType == certificate.CertificateType
            && successor.RevokedAtUtc == null
            && successor.IssuedOn <= certificate.ExpiresOn.AddDays(1)
            && successor.ExpiresOn > certificate.ExpiresOn));
    }
}
