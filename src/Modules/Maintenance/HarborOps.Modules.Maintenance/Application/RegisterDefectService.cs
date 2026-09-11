using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class RegisterDefectService(
    DbContext dbContext,
    IVesselExistenceLookup vesselExistenceLookup,
    TimeProvider timeProvider)
{
    public async Task<DefectDetails> RegisterAsync(
        RegisterDefectRequest request,
        Guid reportedByUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterDefectValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterDefectValidationException(errors);
        }

        var vesselId = request.VesselId!.Value;
        var title = request.Title!.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();
        var reportedSeverity = Enum.Parse<DefectSeverity>(request.ReportedSeverity!, ignoreCase: true);
        var observedAtUtc = request.ObservedAtUtc!.Value;

        var vessel = await vesselExistenceLookup.GetAsync(vesselId, cancellationToken);
        if (vessel is null)
        {
            throw new DefectVesselNotFoundException(vesselId);
        }

        if (request.EquipmentId is { } equipmentId)
        {
            var equipment = await dbContext.Set<Equipment>()
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == equipmentId, cancellationToken);

            if (equipment is null)
            {
                throw new DefectEquipmentNotFoundException(equipmentId);
            }

            if (equipment.VesselId != vesselId)
            {
                throw new DefectEquipmentVesselMismatchException(equipmentId, vesselId);
            }
        }

        var reportedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var defect = VesselDefect.Register(
            vesselId,
            request.EquipmentId,
            title,
            description,
            reportedSeverity,
            observedAtUtc,
            reportedByUserId,
            reportedAtUtc);

        await dbContext.Set<VesselDefect>().AddAsync(defect, cancellationToken);
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
