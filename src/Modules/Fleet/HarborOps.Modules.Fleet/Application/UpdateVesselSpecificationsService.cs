using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

public sealed class UpdateVesselSpecificationsService(
    DbContext dbContext,
    TimeProvider timeProvider,
    IGetVesselDetailsQuery getVesselDetailsQuery)
{
    public async Task<VesselDetailsResponse> UpdateAsync(
        Guid vesselId,
        UpdateVesselSpecificationsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = UpdateVesselSpecificationsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new UpdateVesselSpecificationsValidationException(errors);
        }

        var vessel = await dbContext.Set<Vessel>()
            .FirstOrDefaultAsync(candidate => candidate.Id == vesselId, cancellationToken)
            ?? throw new VesselNotFoundException(vesselId);

        var changed = vessel.UpdateSpecifications(
            request.Dwt,
            request.Gt,
            request.Loa,
            request.Beam,
            request.Draft,
            timeProvider.GetUtcNow().UtcDateTime);

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await getVesselDetailsQuery.GetAsync(vesselId, cancellationToken)
            ?? throw new VesselNotFoundException(vesselId);
    }
}
