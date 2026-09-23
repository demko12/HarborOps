using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class GetPscInspectionDetailQuery(DbContext dbContext) : IGetPscInspectionDetailQuery
{
    public async Task<GetPscInspectionDetailResponse?> GetAsync(int pscInspectionId, CancellationToken cancellationToken)
    {
        var inspection = await dbContext.Set<PscInspection>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == pscInspectionId)
            .Select(candidate => new { candidate.Id, candidate.VesselId, candidate.PortId, candidate.Date, candidate.InspectingAuthority, candidate.DeficiencyCount, candidate.VesselDetained })
            .SingleOrDefaultAsync(cancellationToken);
        if (inspection is null) return null;

        var counts = await dbContext.Set<NonConformity>()
            .AsNoTracking()
            .Where(nonConformity => nonConformity.PscId == pscInspectionId)
            .GroupBy(_ => 1)
            .Select(group => new { Registered = group.Count(), Open = group.Count(item => item.ClosureStatus != NonConformity.ClosureStatusClosed) })
            .SingleOrDefaultAsync(cancellationToken);
        var history = await PscInspectionWorkflowQueries.GetHistoryAsync(dbContext, pscInspectionId, cancellationToken);
        var latest = history.FirstOrDefault();
        var state = PscDetentionState.FromLatestDecision(latest?.Decision, inspection.VesselDetained);
        var registered = counts?.Registered ?? 0;
        var open = counts?.Open ?? 0;

        return new GetPscInspectionDetailResponse(
            inspection.Id,
            inspection.VesselId,
            inspection.PortId,
            inspection.Date,
            inspection.InspectingAuthority,
            inspection.DeficiencyCount,
            registered,
            open,
            registered - open,
            inspection.DeficiencyCount != registered,
            state,
            latest,
            history);
    }
}

internal sealed class GetPscDeficienciesQuery(DbContext dbContext) : IGetPscDeficienciesQuery
{
    public async Task<GetPscDeficienciesResponse?> GetAsync(int pscInspectionId, CancellationToken cancellationToken)
    {
        var inspectionExists = await dbContext.Set<PscInspection>().AsNoTracking().AnyAsync(candidate => candidate.Id == pscInspectionId, cancellationToken);
        if (!inspectionExists) return null;
        var items = await dbContext.Set<NonConformity>()
            .AsNoTracking()
            .Where(nonConformity => nonConformity.PscId == pscInspectionId)
            .OrderBy(nonConformity => nonConformity.CorrectiveActionDeadline)
            .ThenBy(nonConformity => nonConformity.Id)
            .Select(nonConformity => new PscDeficiencyItem(
                nonConformity.Id,
                nonConformity.PscDeficiencyCode ?? string.Empty,
                nonConformity.PscDeficiencyDescription ?? string.Empty,
                nonConformity.Severity,
                nonConformity.CorrectiveActionDeadline,
                nonConformity.ClosureStatus,
                nonConformity.EvidenceDocumentId,
                nonConformity.CorrectiveActions.Count))
            .ToListAsync(cancellationToken);
        return new GetPscDeficienciesResponse(items);
    }
}

internal sealed class GetPscDetentionHistoryQuery(DbContext dbContext) : IGetPscDetentionHistoryQuery
{
    public async Task<IReadOnlyList<PscDetentionDecisionItem>?> GetAsync(int pscInspectionId, CancellationToken cancellationToken)
    {
        var inspectionExists = await dbContext.Set<PscInspection>().AsNoTracking().AnyAsync(candidate => candidate.Id == pscInspectionId, cancellationToken);
        return inspectionExists ? await PscInspectionWorkflowQueries.GetHistoryAsync(dbContext, pscInspectionId, cancellationToken) : null;
    }
}

internal static class PscInspectionWorkflowQueries
{
    internal static Task<List<PscDetentionDecisionItem>> GetHistoryAsync(DbContext dbContext, int pscInspectionId, CancellationToken cancellationToken) =>
        dbContext.Set<PscDetentionDecision>()
            .AsNoTracking()
            .Where(decision => decision.PscInspectionId == pscInspectionId)
            .OrderByDescending(decision => decision.OccurredAtUtc)
            .ThenByDescending(decision => decision.Id)
            .Select(decision => new PscDetentionDecisionItem(
                decision.Id,
                decision.Decision,
                decision.AuthorityReference,
                decision.Reason,
                decision.ReleaseDate,
                decision.DocumentId,
                decision.ActorUserId,
                decision.OccurredAtUtc))
            .ToListAsync(cancellationToken);
}
