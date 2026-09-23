using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class SaveReeferMonitoringProfileService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<ReeferMonitoringProfileDetails> SaveAsync(
        Guid containerId,
        Guid actorUserId,
        SaveReeferMonitoringProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var container = await ReeferMonitoringGuard.FindReeferContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var profile = await ReeferMonitoringGuard.FindProfileAsync(
            dbContext,
            containerId,
            tracked: true,
            cancellationToken);

        var errors = ReeferMonitoringValidator.ValidateProfile(
            request,
            requiresReason: profile is not null);

        if (errors.Count > 0)
        {
            throw new ReeferMonitoringValidationException(errors);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var minimumCelsius = request.MinimumCelsius!.Value;
        var maximumCelsius = request.MaximumCelsius!.Value;
        var targetCelsius = request.TargetCelsius!.Value;

        ReeferProfileChange change;

        if (profile is null)
        {
            profile = ReeferMonitoringProfile.Configure(
                container.Id,
                container.IsReefer,
                minimumCelsius,
                maximumCelsius,
                targetCelsius,
                actorUserId,
                utcNow);

            change = profile.RecordConfiguration(request.Reason);

            await dbContext.Set<ReeferMonitoringProfile>().AddAsync(profile, cancellationToken);
        }
        else
        {
            change = profile.Revise(
                minimumCelsius,
                maximumCelsius,
                targetCelsius,
                request.Reason!,
                actorUserId,
                utcNow);
        }

        await dbContext.Set<ReeferProfileChange>().AddAsync(change, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ReeferMonitoringMapper.ToDetails(profile);
    }
}
