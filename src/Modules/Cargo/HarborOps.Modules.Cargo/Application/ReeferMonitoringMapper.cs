using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class ReeferMonitoringMapper
{
    internal static ReeferMonitoringProfileDetails ToDetails(ReeferMonitoringProfile profile)
    {
        return new ReeferMonitoringProfileDetails(
            profile.ContainerId,
            profile.MinimumCelsius,
            profile.MaximumCelsius,
            profile.TargetCelsius,
            profile.Version,
            profile.CreatedByUserId,
            profile.CreatedAtUtc,
            profile.UpdatedByUserId,
            profile.UpdatedAtUtc);
    }

    internal static ReeferProfileChangeListItem ToListItem(ReeferProfileChange change)
    {
        return new ReeferProfileChangeListItem(
            change.Id,
            change.Version,
            change.FromMinimumCelsius,
            change.FromMaximumCelsius,
            change.FromTargetCelsius,
            change.ToMinimumCelsius,
            change.ToMaximumCelsius,
            change.ToTargetCelsius,
            change.Reason,
            change.ActorUserId,
            change.OccurredUtc);
    }

    internal static ReeferTemperatureReadingListItem ToListItem(ReeferTemperatureReading reading)
    {
        return new ReeferTemperatureReadingListItem(
            reading.Id,
            reading.ContainerId,
            reading.TemperatureCelsius,
            reading.MeasuredAtUtc,
            reading.RecordedAtUtc,
            reading.Notes,
            reading.RecordedByUserId,
            reading.ThresholdMinimumCelsius,
            reading.ThresholdMaximumCelsius,
            reading.ThresholdTargetCelsius,
            reading.ThresholdProfileVersion,
            reading.Status.ToString(),
            reading.IsWithinThresholds,
            reading.CorrectsReadingId,
            reading.SupersededByReadingId,
            reading.CorrectionReason,
            reading.CorrectedByUserId,
            reading.CorrectedAtUtc,
            reading.VoidReason,
            reading.VoidedByUserId,
            reading.VoidedAtUtc);
    }

    internal static ReeferExcursionListItem ToListItem(ReeferExcursion excursion)
    {
        return new ReeferExcursionListItem(
            excursion.Id,
            excursion.ContainerId,
            excursion.ReadingId,
            excursion.Direction.ToString(),
            excursion.TemperatureCelsius,
            excursion.ThresholdMinimumCelsius,
            excursion.ThresholdMaximumCelsius,
            excursion.MeasuredAtUtc,
            excursion.DetectedAtUtc,
            excursion.Status.ToString(),
            excursion.Disposition?.ToString(),
            excursion.Explanation,
            excursion.ResolvedByUserId,
            excursion.ResolvedAtUtc,
            excursion.SupersededByExcursionId,
            excursion.SupersededAtUtc);
    }
}
