using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetDefectsQuery(DbContext dbContext)
{
    public async Task<GetDefectsResponse> GetDefectsAsync(
        GetDefectsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetDefectsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetDefectsValidationException(errors);
        }

        var defects = dbContext.Set<VesselDefect>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = Enum.Parse<DefectStatus>(request.Status, ignoreCase: true);
            defects = defects.Where(defect => defect.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Severity))
        {
            var severity = Enum.Parse<DefectSeverity>(request.Severity, ignoreCase: true);
            defects = defects.Where(defect =>
                defect.ConfirmedSeverity == severity
                || (defect.ConfirmedSeverity == null && defect.ReportedSeverity == severity));
        }

        if (request.VesselId.HasValue)
        {
            defects = defects.Where(defect => defect.VesselId == request.VesselId.Value);
        }

        var totalCount = await defects.CountAsync(cancellationToken);

        var rows = await defects
            .OrderByDescending(defect => defect.ReportedAtUtc)
            .ThenBy(defect => defect.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(defect => new
            {
                defect.Id,
                defect.VesselId,
                defect.EquipmentId,
                defect.Title,
                defect.ReportedSeverity,
                defect.ConfirmedSeverity,
                defect.Priority,
                defect.Status,
                defect.ObservedAtUtc,
                defect.ReportedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new DefectSummary(
                row.Id,
                row.VesselId,
                row.EquipmentId,
                row.Title,
                row.ReportedSeverity.ToString(),
                row.ConfirmedSeverity?.ToString(),
                row.Priority?.ToString(),
                row.Status.ToString(),
                row.ObservedAtUtc,
                row.ReportedAtUtc))
            .ToList();

        return new GetDefectsResponse(items, request.PageNumber, request.PageSize, totalCount);
    }
}
