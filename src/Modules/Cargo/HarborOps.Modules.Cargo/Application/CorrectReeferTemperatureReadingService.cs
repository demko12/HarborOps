using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class CorrectReeferTemperatureReadingService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<RecordReeferTemperatureReadingResponse> CorrectAsync(
        Guid containerId,
        Guid readingId,
        Guid actorUserId,
        CorrectReeferTemperatureReadingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await ReeferMonitoringGuard.FindReeferContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var errors = ReeferMonitoringValidator.ValidateCorrection(request, utcNow);
        if (errors.Count > 0)
        {
            throw new ReeferMonitoringValidationException(errors);
        }

        var original = await ReeferMonitoringGuard.FindReadingAsync(
            dbContext,
            containerId,
            readingId,
            cancellationToken);

        if (!original.IsEffective)
        {
            throw new ReeferReadingTransitionException(
                original.Id,
                original.Status,
                ReeferReadingNotEffectiveException.CorrectAction);
        }

        var supersededExcursion = await ReeferMonitoringGuard.FindOpenExcursionForReadingAsync(
            dbContext,
            original.Id,
            cancellationToken);

        var replacement = original.Correct(
            request.TemperatureCelsius!.Value,
            ReeferMonitoringValidator.NormalizeToUtc(request.MeasuredAtUtc!.Value),
            request.Notes,
            request.Reason!,
            actorUserId,
            utcNow);

        await dbContext.Set<ReeferTemperatureReading>().AddAsync(replacement, cancellationToken);

        ReeferExcursion? excursion = null;

        if (replacement.OpensExcursion)
        {
            excursion = ReeferExcursion.Open(replacement);

            await dbContext.Set<ReeferExcursion>().AddAsync(excursion, cancellationToken);
        }

        supersededExcursion?.Supersede(excursion?.Id, utcNow);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordReeferTemperatureReadingResponse(
            ReeferMonitoringMapper.ToListItem(replacement),
            excursion is null ? null : ReeferMonitoringMapper.ToListItem(excursion));
    }
}
