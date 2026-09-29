using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetCrewCertificateHistoryQuery(DbContext dbContext) : IGetCrewCertificateHistoryQuery
{
    private const int MaxChainLength = 50;

    public async Task<GetCrewCertificateHistoryResponse> GetAsync(Guid certificateId, CancellationToken cancellationToken)
    {
        var certificate = await dbContext.Set<CrewCertificate>()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == certificateId, cancellationToken)
            ?? throw new CrewCertificateNotFoundException(certificateId);

        // A renewal chain never spans members or certificate types (enforced by construction in
        // RenewCrewCertificateService), so every link is found within this single, small result set.
        var relatedCertificates = await dbContext.Set<CrewCertificate>()
            .AsNoTracking()
            .Where(candidate => candidate.CrewMemberId == certificate.CrewMemberId && candidate.Type == certificate.Type)
            .Select(candidate => new { candidate.Id, candidate.PredecessorCertificateId, candidate.IssueDate })
            .ToListAsync(cancellationToken);

        var successorByPredecessorId = relatedCertificates
            .Where(candidate => candidate.PredecessorCertificateId is not null)
            .ToDictionary(candidate => candidate.PredecessorCertificateId!.Value, candidate => candidate.Id);
        var predecessorById = relatedCertificates.ToDictionary(candidate => candidate.Id, candidate => candidate.PredecessorCertificateId);

        var chainIds = new HashSet<Guid> { certificateId };

        var ancestorId = predecessorById.GetValueOrDefault(certificateId);
        for (var hops = 0; ancestorId is { } id && hops < MaxChainLength; hops++)
        {
            chainIds.Add(id);
            ancestorId = predecessorById.GetValueOrDefault(id);
        }

        var successorId = successorByPredecessorId.GetValueOrDefault(certificateId);
        for (var hops = 0; successorId is { } id && hops < MaxChainLength; hops++)
        {
            chainIds.Add(id);
            successorId = successorByPredecessorId.GetValueOrDefault(id);
        }

        var chainCertificateIds = relatedCertificates
            .Where(candidate => chainIds.Contains(candidate.Id))
            .OrderBy(candidate => candidate.IssueDate)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => candidate.Id)
            .ToList();

        var history = await dbContext.Set<CrewCertificateHistory>()
            .AsNoTracking()
            .Where(entry => chainIds.Contains(entry.CertificateId))
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new CrewCertificateHistoryItem(
                entry.Id,
                entry.CertificateId,
                entry.EventType,
                entry.ActorUserId,
                entry.OccurredAtUtc,
                entry.Reason,
                entry.PredecessorCertificateId))
            .ToListAsync(cancellationToken);

        return new GetCrewCertificateHistoryResponse(certificateId, chainCertificateIds, history);
    }
}
