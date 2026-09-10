using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class RejectDefectService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<DefectDetails> RejectAsync(
        Guid defectId,
        RejectDefectRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RejectDefectValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RejectDefectValidationException(errors);
        }

        var defect = await dbContext.Set<VesselDefect>()
            .SingleOrDefaultAsync(candidate => candidate.Id == defectId, cancellationToken);

        if (defect is null)
        {
            throw new DefectNotFoundException(defectId);
        }

        var reason = request.Reason!.Trim();
        var atUtc = timeProvider.GetUtcNow().UtcDateTime;

        defect.Reject(reason, actorUserId, atUtc);

        await dbContext.SaveChangesAsync(cancellationToken);

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
