using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

public sealed class UpdateVesselRegistryDetailsService(
    DbContext dbContext,
    TimeProvider timeProvider,
    IGetVesselDetailsQuery getVesselDetailsQuery)
{
    public async Task<VesselDetailsResponse> UpdateAsync(
        Guid vesselId,
        UpdateVesselRegistryDetailsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = UpdateVesselRegistryDetailsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new UpdateVesselRegistryDetailsValidationException(errors);
        }

        var vesselTypeId = request.VesselTypeId!.Value;
        var name = request.Name!.Trim();
        var flagState = request.FlagState!.Trim();
        var mmsi = Normalize(request.Mmsi);
        var callSign = Normalize(request.CallSign);
        var portOfRegistry = Normalize(request.PortOfRegistry);
        var classificationSociety = Normalize(request.ClassificationSociety);
        var lifecycleStage = Normalize(request.LifecycleStage);
        var msmdId = Normalize(request.MsmdId);

        var vessel = await dbContext.Set<Vessel>()
            .FirstOrDefaultAsync(candidate => candidate.Id == vesselId, cancellationToken)
            ?? throw new VesselNotFoundException(vesselId);

        var vesselTypeExists = await dbContext.Set<VesselType>()
            .AsNoTracking()
            .AnyAsync(vesselType => vesselType.Id == vesselTypeId, cancellationToken);

        if (!vesselTypeExists)
        {
            throw new VesselTypeNotFoundException(vesselTypeId);
        }

        await GuardUniqueIdentifiersAsync(vesselId, mmsi, callSign, cancellationToken);

        var changed = vessel.UpdateRegistryDetails(
            vesselTypeId,
            name,
            mmsi,
            callSign,
            flagState,
            portOfRegistry,
            classificationSociety,
            lifecycleStage,
            msmdId,
            timeProvider.GetUtcNow().UtcDateTime);

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await getVesselDetailsQuery.GetAsync(vesselId, cancellationToken)
            ?? throw new VesselNotFoundException(vesselId);
    }

    private async Task GuardUniqueIdentifiersAsync(
        Guid vesselId,
        string? mmsi,
        string? callSign,
        CancellationToken cancellationToken)
    {
        var others = dbContext.Set<Vessel>()
            .AsNoTracking()
            .Where(candidate => candidate.Id != vesselId);

        if (mmsi is not null)
        {
            var normalizedMmsi = mmsi.ToUpperInvariant();
            var mmsiTaken = await others.AnyAsync(
                candidate => candidate.Mmsi != null
                    && candidate.Mmsi.ToUpper() == normalizedMmsi,
                cancellationToken);

            if (mmsiTaken)
            {
                throw new DuplicateMmsiException(mmsi);
            }
        }

        if (callSign is not null)
        {
            var normalizedCallSign = callSign.ToUpperInvariant();
            var callSignTaken = await others.AnyAsync(
                candidate => candidate.CallSign != null
                    && candidate.CallSign.ToUpper() == normalizedCallSign,
                cancellationToken);

            if (callSignTaken)
            {
                throw new DuplicateCallSignException(callSign);
            }
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
