using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class GetNonConformitiesQuery(DbContext dbContext, TimeProvider timeProvider)
    : IGetNonConformitiesQuery
{
    public async Task<GetNonConformitiesResponse> GetAsync(
        GetNonConformitiesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetNonConformitiesValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetNonConformitiesValidationException(errors);
        }

        var nonConformities = dbContext.Set<NonConformity>().AsNoTracking().AsQueryable();

        if (request.Severity is not null)
        {
            nonConformities = nonConformities.Where(nonConformity => nonConformity.Severity == request.Severity);
        }

        if (request.ClosureStatus is not null)
        {
            nonConformities = nonConformities.Where(nonConformity => nonConformity.ClosureStatus == request.ClosureStatus);
        }

        var rows = await nonConformities
            .Select(nonConformity => new
            {
                nonConformity.Id,
                VesselId = nonConformity.Psc != null
                    ? nonConformity.Psc.VesselId
                    : nonConformity.Survey!.VesselId,
                nonConformity.SurveyId,
                nonConformity.PscId,
                nonConformity.Severity,
                nonConformity.ClosureStatus,
                nonConformity.CorrectiveActionDeadline,
                nonConformity.EvidenceDocumentId,
                CorrectiveActionCount = nonConformity.CorrectiveActions.Count,
            })
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var projectedRows = rows
            .Select(row => new
            {
                row.Id,
                row.VesselId,
                row.SurveyId,
                row.PscId,
                row.Severity,
                row.ClosureStatus,
                row.CorrectiveActionDeadline,
                IsOverdue = row.ClosureStatus != NonConformity.ClosureStatusClosed
                    && row.CorrectiveActionDeadline < today,
                row.EvidenceDocumentId,
                row.CorrectiveActionCount,
            })
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var normalizedSearch = request.Search.Trim().ToUpperInvariant();
            projectedRows = projectedRows.Where(
                row => row.EvidenceDocumentId.ToString().ToUpperInvariant().Contains(normalizedSearch));
        }

        if (request.Overdue is { } overdue)
        {
            projectedRows = projectedRows.Where(row => row.IsOverdue == overdue);
        }

        var filteredRows = projectedRows.ToList();
        var totalCount = filteredRows.Count;

        var items = filteredRows
            .OrderBy(row => row.CorrectiveActionDeadline)
            .ThenBy(row => row.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(row => new NonConformityListItem(
                row.Id,
                row.VesselId,
                row.SurveyId,
                row.PscId,
                row.Severity,
                row.ClosureStatus,
                row.CorrectiveActionDeadline,
                row.IsOverdue,
                row.EvidenceDocumentId,
                row.CorrectiveActionCount))
            .ToList();

        return new GetNonConformitiesResponse(items, totalCount, request.PageNumber, request.PageSize);
    }

    public async Task<GetNonConformitiesSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var counts = await dbContext.Set<NonConformity>()
            .AsNoTracking()
            .Where(nonConformity => nonConformity.ClosureStatus != NonConformity.ClosureStatusClosed)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Critical = group.Count(nonConformity => nonConformity.Severity == NonConformity.SeverityCritical),
                Major = group.Count(nonConformity => nonConformity.Severity == NonConformity.SeverityMajor),
                Minor = group.Count(nonConformity => nonConformity.Severity == NonConformity.SeverityMinor),
                Overdue = group.Count(nonConformity => nonConformity.CorrectiveActionDeadline < today),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new GetNonConformitiesSummaryResponse(
            counts?.Critical ?? 0,
            counts?.Major ?? 0,
            counts?.Minor ?? 0,
            counts?.Overdue ?? 0);
    }
}
