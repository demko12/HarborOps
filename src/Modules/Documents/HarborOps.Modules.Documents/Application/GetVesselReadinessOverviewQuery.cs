using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetVesselReadinessOverviewQuery(
    DbContext dbContext,
    IVesselDirectory vesselDirectory,
    TimeProvider timeProvider)
    : IGetVesselReadinessOverviewQuery
{
    public async Task<GetVesselReadinessOverviewResponse> GetAsync(
        GetVesselReadinessOverviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var errors = GetDocumentReadinessQuery.Validate(
            new GetDocumentReadinessRequest
            {
                RequiredThrough = request.RequiredThrough,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
            },
            today);

        if (errors.Count > 0)
        {
            throw new GetDocumentReadinessValidationException(errors);
        }

        var requiredThrough = request.RequiredThrough ?? today;
        var pageNumber = request.PageNumber ?? GetDocumentReadinessQuery.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetDocumentReadinessQuery.DefaultPageSize;

        var vessels = await vesselDirectory.GetPageAsync(pageNumber, pageSize, cancellationToken);
        var vesselIds = vessels.Items.Select(vessel => vessel.VesselId).ToList();

        var requirements = dbContext.Set<VesselDocumentRequirement>()
            .AsNoTracking()
            .Where(requirement => requirement.IsEnabled && vesselIds.Contains(requirement.VesselId));

        var rows = vesselIds.Count == 0
            ? []
            : await GetDocumentReadinessQuery.RequirementRows(dbContext, requirements, today, requiredThrough)
                .Select(row => new { row.VesselId, row.Satisfied })
                .ToListAsync(cancellationToken);

        var items = vessels.Items
            .Select(vessel =>
            {
                var configuredCount = rows.Count(row => row.VesselId == vessel.VesselId);
                var satisfiedCount = rows.Count(row => row.VesselId == vessel.VesselId && row.Satisfied);

                return new VesselReadinessOverviewItem(
                    vessel.VesselId,
                    vessel.Name,
                    DocumentReadinessRules.VesselStatus(configuredCount, satisfiedCount),
                    new DocumentReadinessSummary(configuredCount, satisfiedCount, configuredCount - satisfiedCount));
            })
            .ToList();

        return new GetVesselReadinessOverviewResponse(
            today,
            requiredThrough,
            items,
            pageNumber,
            pageSize,
            vessels.TotalCount);
    }
}
