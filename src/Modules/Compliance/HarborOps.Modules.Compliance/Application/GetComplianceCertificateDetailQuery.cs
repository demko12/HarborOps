using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class GetComplianceCertificateDetailQuery(DbContext dbContext, TimeProvider timeProvider)
    : IGetComplianceCertificateDetailQuery
{
    internal const int MaxRelatedCertificates = 500;

    public async Task<GetComplianceCertificateDetailResponse?> GetAsync(
        Guid certificateId,
        CancellationToken cancellationToken)
    {
        var certificate = await dbContext.Set<ComplianceCertificate>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == certificateId, cancellationToken);

        if (certificate is null)
        {
            return null;
        }

        var related = await dbContext.Set<ComplianceCertificate>()
            .AsNoTracking()
            .Where(candidate => candidate.VesselId == certificate.VesselId
                && candidate.CertificateType == certificate.CertificateType)
            .OrderBy(candidate => candidate.CreatedUtc)
            .ThenBy(candidate => candidate.Id)
            .Take(MaxRelatedCertificates)
            .ToListAsync(cancellationToken);

        if (related.All(candidate => candidate.Id != certificate.Id))
        {
            related.Add(certificate);
        }

        var chain = BuildChain(certificate, related);
        var chainIds = chain.Select(item => item.Id).ToList();
        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var history = await dbContext.Set<ComplianceCertificateHistory>()
            .AsNoTracking()
            .Where(entry => chainIds.Contains(entry.CertificateId))
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new ComplianceCertificateHistoryItem(
                entry.Id,
                entry.CertificateId,
                entry.EventType,
                entry.ActorUserId,
                entry.OccurredAtUtc,
                entry.Reason,
                entry.SuccessorCertificateId))
            .ToListAsync(cancellationToken);

        var successor = DirectSuccessor(certificate, related);
        var hasAnySuccessor = related.Any(candidate => candidate.PredecessorCertificateId == certificate.Id);

        return new GetComplianceCertificateDetailResponse(
            certificate.Id,
            certificate.VesselId,
            certificate.CertificateType,
            certificate.CertificateNumber,
            certificate.IssuingAuthority,
            certificate.IssuedOn,
            certificate.ExpiresOn,
            ComplianceCertificateValidity.Status(certificate.IssuedOn, certificate.ExpiresOn, certificate.IsRevoked, asOf),
            certificate.CreatedUtc,
            certificate.PredecessorCertificateId,
            successor?.Id,
            certificate.RevokedAtUtc,
            certificate.RevokedByUserId,
            certificate.RevocationReason,
            !certificate.IsRevoked && !hasAnySuccessor,
            !certificate.IsRevoked,
            certificate.ConcurrencyToken,
            chain.Select((item, index) => ToChainItem(item, index == 0 ? null : chain[index - 1], asOf)).ToList(),
            history);
    }

    internal static IReadOnlyList<ComplianceCertificate> BuildChain(
        ComplianceCertificate certificate,
        IReadOnlyList<ComplianceCertificate> related)
    {
        var byId = related.ToDictionary(candidate => candidate.Id);
        var visited = new HashSet<Guid> { certificate.Id };
        var ancestors = new List<ComplianceCertificate>();
        var current = certificate;

        while (current.PredecessorCertificateId is { } predecessorId
            && byId.TryGetValue(predecessorId, out var predecessor)
            && current.IsDirectSuccessorOf(predecessor)
            && visited.Add(predecessor.Id))
        {
            ancestors.Add(predecessor);
            current = predecessor;
        }

        ancestors.Reverse();
        var chain = new List<ComplianceCertificate>(ancestors) { certificate };

        current = certificate;
        while (DirectSuccessor(current, related) is { } successor && visited.Add(successor.Id))
        {
            chain.Add(successor);
            current = successor;
        }

        return chain;
    }

    private static ComplianceCertificate? DirectSuccessor(
        ComplianceCertificate certificate,
        IReadOnlyList<ComplianceCertificate> related)
    {
        return related
            .Where(candidate => candidate.IsDirectSuccessorOf(certificate))
            .OrderBy(candidate => candidate.CreatedUtc)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();
    }

    private static ComplianceCertificateChainItem ToChainItem(
        ComplianceCertificate item,
        ComplianceCertificate? predecessor,
        DateOnly asOf)
    {
        return new ComplianceCertificateChainItem(
            item.Id,
            item.CertificateNumber,
            item.IssuingAuthority,
            item.IssuedOn,
            item.ExpiresOn,
            ComplianceCertificateValidity.Status(item.IssuedOn, item.ExpiresOn, item.IsRevoked, asOf),
            item.RevokedAtUtc,
            item.PredecessorCertificateId,
            predecessor is null ? null : ComplianceCertificateValidity.GapDays(predecessor.ExpiresOn, item.IssuedOn),
            predecessor is not null && ComplianceCertificateValidity.CoversExpiry(
                predecessor.ExpiresOn, item.IssuedOn, item.ExpiresOn, item.IsRevoked));
    }
}
