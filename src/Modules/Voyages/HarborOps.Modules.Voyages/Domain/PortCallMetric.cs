namespace HarborOps.Modules.Voyages.Domain;

internal sealed record PortCallMetric(int? Minutes, string Quality, string? Milestone)
{
    internal static PortCallMetric Measured(int minutes) =>
        new(minutes, PortCallMetricQuality.Valid, null);

    internal static PortCallMetric Unavailable(string quality, string milestone) =>
        new(null, quality, milestone);

    internal static PortCallMetric InvalidChronology() =>
        new(null, PortCallMetricQuality.InvalidChronology, null);
}
