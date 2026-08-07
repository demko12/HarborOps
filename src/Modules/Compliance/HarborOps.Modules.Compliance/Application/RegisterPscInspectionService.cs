using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

public sealed class RegisterPscInspectionService(DbContext dbContext)
{
    public async Task<RegisterPscInspectionResponse> RegisterAsync(
        RegisterPscInspectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterPscInspectionValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterPscInspectionValidationException(errors);
        }

        var vesselId = request.VesselId!.Value;
        var portId = request.PortId!.Value;
        var inspectionDate = request.InspectionDate!.Value;
        var inspectingAuthority = request.InspectingAuthority!.Trim();
        var deficiencyCount = request.DeficiencyCount!.Value;
        var vesselDetained = request.VesselDetained!.Value;

        var inspection = new PscInspection(
            vesselId,
            portId,
            inspectionDate,
            inspectingAuthority,
            deficiencyCount,
            vesselDetained);

        var inspections = dbContext.Set<PscInspection>();

        await inspections.AddAsync(inspection, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterPscInspectionResponse(inspection.Id);
    }
}
