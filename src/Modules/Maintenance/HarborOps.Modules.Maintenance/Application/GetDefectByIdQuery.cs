using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetDefectByIdQuery(DbContext dbContext)
{
    public async Task<DefectDetails> GetByIdAsync(
        Guid defectId,
        CancellationToken cancellationToken)
    {
        var defect = await dbContext.Set<VesselDefect>()
            .AsNoTracking()
            .Where(item => item.Id == defectId)
            .Select(item => new
            {
                item.Id,
                item.VesselId,
                item.EquipmentId,
                item.Title,
                item.Description,
                item.ReportedSeverity,
                item.ObservedAtUtc,
                item.Status,
                item.ReportedByUserId,
                item.ReportedAtUtc,
                item.ConfirmedSeverity,
                item.Priority,
                item.TriageNotes,
                item.TriagedByUserId,
                item.TriagedAtUtc,
                item.RejectionReason,
                item.RejectedByUserId,
                item.RejectedAtUtc,
                item.WorkOrderId,
                item.ConvertedByUserId,
                item.ConvertedAtUtc,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (defect is null)
        {
            throw new DefectNotFoundException(defectId);
        }

        return new DefectDetails(
            defect.Id,
            defect.VesselId,
            defect.EquipmentId,
            defect.Title,
            defect.Description,
            defect.ReportedSeverity.ToString(),
            defect.ObservedAtUtc,
            defect.Status.ToString(),
            defect.ReportedByUserId,
            defect.ReportedAtUtc,
            defect.ConfirmedSeverity?.ToString(),
            defect.Priority?.ToString(),
            defect.TriageNotes,
            defect.TriagedByUserId,
            defect.TriagedAtUtc,
            defect.RejectionReason,
            defect.RejectedByUserId,
            defect.RejectedAtUtc,
            defect.WorkOrderId,
            defect.ConvertedByUserId,
            defect.ConvertedAtUtc);
    }
}
