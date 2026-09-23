using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class RecordReeferTemperatureReadingService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<RecordReeferTemperatureReadingResponse> RecordAsync(
        Guid containerId,
        Guid actorUserId,
        RecordReeferTemperatureReadingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await ReeferMonitoringGuard.FindReeferContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var errors = ReeferMonitoringValidator.ValidateReading(request, utcNow);
        if (errors.Count > 0)
        {
            throw new ReeferMonitoringValidationException(errors);
        }

        var profile = await ReeferMonitoringGuard.RequireProfileAsync(
            dbContext,
            containerId,
            tracked: false,
            cancellationToken);

        var reading = ReeferTemperatureReading.Record(
            profile,
            request.TemperatureCelsius!.Value,
            ReeferMonitoringValidator.NormalizeToUtc(request.MeasuredAtUtc!.Value),
            request.Notes,
            actorUserId,
            utcNow);

        await dbContext.Set<ReeferTemperatureReading>().AddAsync(reading, cancellationToken);

        ReeferExcursion? excursion = null;

        if (reading.OpensExcursion)
        {
            excursion = ReeferExcursion.Open(reading);

            await dbContext.Set<ReeferExcursion>().AddAsync(excursion, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordReeferTemperatureReadingResponse(
            ReeferMonitoringMapper.ToListItem(reading),
            excursion is null ? null : ReeferMonitoringMapper.ToListItem(excursion));
    }
}
