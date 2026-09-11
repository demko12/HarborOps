using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class TriageDefectService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<DefectDetails> TriageAsync(
        Guid defectId,
        TriageDefectRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = TriageDefectValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new TriageDefectValidationException(errors);
        }

        var defect = await dbContext.Set<VesselDefect>()
            .SingleOrDefaultAsync(candidate => candidate.Id == defectId, cancellationToken);

        if (defect is null)
        {
            throw new DefectNotFoundException(defectId);
        }

        var confirmedSeverity = Enum.Parse<DefectSeverity>(request.ConfirmedSeverity!, ignoreCase: true);
        var priority = Enum.Parse<WorkOrderPriority>(request.Priority!, ignoreCase: true);
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var atUtc = timeProvider.GetUtcNow().UtcDateTime;

        defect.Triage(confirmedSeverity, priority, notes, actorUserId, atUtc);

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
