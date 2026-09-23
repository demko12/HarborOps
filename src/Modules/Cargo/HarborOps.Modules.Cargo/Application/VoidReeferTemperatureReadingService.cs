using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class VoidReeferTemperatureReadingService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<ReeferTemperatureReadingListItem> VoidAsync(
        Guid containerId,
        Guid readingId,
        Guid actorUserId,
        VoidReeferTemperatureReadingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await ReeferMonitoringGuard.FindReeferContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var errors = ReeferMonitoringValidator.ValidateVoid(request);
        if (errors.Count > 0)
        {
            throw new ReeferMonitoringValidationException(errors);
        }

        var reading = await ReeferMonitoringGuard.FindReadingAsync(
            dbContext,
            containerId,
            readingId,
            cancellationToken);

        if (!reading.IsEffective)
        {
            throw new ReeferReadingTransitionException(
                reading.Id,
                reading.Status,
                ReeferReadingNotEffectiveException.VoidAction);
        }

        var supersededExcursion = await ReeferMonitoringGuard.FindOpenExcursionForReadingAsync(
            dbContext,
            reading.Id,
            cancellationToken);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        reading.Void(request.Reason!, actorUserId, utcNow);
        supersededExcursion?.Supersede(null, utcNow);

        await dbContext.SaveChangesAsync(cancellationToken);

        return ReeferMonitoringMapper.ToListItem(reading);
    }
}
