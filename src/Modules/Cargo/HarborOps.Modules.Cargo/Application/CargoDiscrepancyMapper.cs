using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class CargoDiscrepancyMapper
{
    internal const string StartInvestigationFirstBlocker =
        "Start the investigation before resolving the discrepancy.";

    internal const string InvestigationNoteRequiredBlocker =
        "Add at least one investigation note before resolving the discrepancy.";

    internal const string SourceExcursionOpenBlocker =
        "The linked temperature excursion is still open. Resolve it or correct its reading first.";

    internal static CargoDiscrepancyListItem ToListItem(
        CargoDiscrepancy discrepancy,
        string? containerNumber)
    {
        return new CargoDiscrepancyListItem(
            discrepancy.Id,
            discrepancy.CargoBookingId,
            discrepancy.ContainerId,
            containerNumber,
            discrepancy.Type.ToString(),
            discrepancy.Severity.ToString(),
            discrepancy.Description,
            discrepancy.ObservedAtUtc,
            discrepancy.ReportedByUserId,
            discrepancy.ReportedByName,
            discrepancy.ReportedAtUtc,
            discrepancy.Status.ToString(),
            discrepancy.Disposition?.ToString(),
            discrepancy.ResolvedByUserId,
            discrepancy.ResolvedByName,
            discrepancy.ResolvedAtUtc,
            discrepancy.VoidedByUserId,
            discrepancy.VoidedByName,
            discrepancy.VoidedAtUtc,
            discrepancy.SourceExcursionId);
    }

    internal static CargoDiscrepancyEventListItem ToListItem(CargoDiscrepancyEvent entry)
    {
        return new CargoDiscrepancyEventListItem(
            entry.Id,
            entry.EventType.ToString(),
            entry.FromStatus?.ToString(),
            entry.ToStatus?.ToString(),
            entry.Text,
            entry.ActorUserId,
            entry.ActorName,
            entry.OccurredAtUtc);
    }

    internal static CargoDiscrepancyDetails ToDetails(
        CargoDiscrepancy discrepancy,
        CargoBooking booking,
        string? containerNumber,
        ReeferExcursion? sourceExcursion,
        IEnumerable<CargoDiscrepancyEvent> history)
    {
        var ordered = history
            .OrderBy(entry => entry.OccurredAtUtc)
            .ThenBy(entry => entry.EventType)
            .ThenBy(entry => entry.Id)
            .ToList();

        var investigationNoteCount = ordered.Count(entry =>
            entry.EventType == CargoDiscrepancyEventType.NoteAdded
            && entry.ToStatus == CargoDiscrepancyStatus.Investigating);

        var sourceBlocksResolution = sourceExcursion is not null
            && discrepancy.IsOpen
            && sourceExcursion.Status == ReeferExcursionStatus.Open;

        return new CargoDiscrepancyDetails(
            ToListItem(discrepancy, containerNumber),
            booking.BookingNumber,
            booking.Status.ToString(),
            ToSource(discrepancy, sourceExcursion, sourceBlocksResolution),
            investigationNoteCount,
            ResolutionBlocker(discrepancy, investigationNoteCount, sourceBlocksResolution),
            [.. ordered.Select(ToListItem)]);
    }

    private static CargoDiscrepancySource? ToSource(
        CargoDiscrepancy discrepancy,
        ReeferExcursion? sourceExcursion,
        bool blocksResolution)
    {
        if (discrepancy.SourceExcursionId is not { } excursionId)
        {
            return null;
        }

        return new CargoDiscrepancySource(
            excursionId,
            sourceExcursion?.ContainerId ?? discrepancy.ContainerId ?? Guid.Empty,
            discrepancy.SourceDirection?.ToString(),
            discrepancy.SourceTemperatureCelsius,
            discrepancy.SourceThresholdMinimumCelsius,
            discrepancy.SourceThresholdMaximumCelsius,
            discrepancy.SourceMeasuredAtUtc,
            discrepancy.SourceDetectedAtUtc,
            sourceExcursion?.Status.ToString() ?? "Unknown",
            sourceExcursion?.Disposition?.ToString(),
            sourceExcursion?.SupersededByExcursionId,
            blocksResolution);
    }

    private static string? ResolutionBlocker(
        CargoDiscrepancy discrepancy,
        int investigationNoteCount,
        bool sourceBlocksResolution)
    {
        return discrepancy.Status switch
        {
            CargoDiscrepancyStatus.Reported => StartInvestigationFirstBlocker,
            CargoDiscrepancyStatus.Investigating when investigationNoteCount == 0 => InvestigationNoteRequiredBlocker,
            CargoDiscrepancyStatus.Investigating when sourceBlocksResolution => SourceExcursionOpenBlocker,
            _ => null,
        };
    }
}
