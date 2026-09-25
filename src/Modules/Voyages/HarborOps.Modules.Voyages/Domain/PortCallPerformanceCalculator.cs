namespace HarborOps.Modules.Voyages.Domain;

internal static class PortCallPerformanceCalculator
{
    internal const string ActualArrivalMilestone = "ActualArrival";
    internal const string ActualDepartureMilestone = "ActualDeparture";

    internal static PortCallPerformanceMetrics Calculate(PortCallPerformanceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var arrival = Anchor(ActualArrivalMilestone, input.ActualArrivalUtc);
        var departure = Anchor(ActualDepartureMilestone, input.ActualDepartureUtc);
        var allFast = Single(PortCallActivityType.AllFast, input.AllFast);
        var cargoStarted = Single(PortCallActivityType.CargoStarted, input.CargoStarted);
        var cargoCompleted = Single(PortCallActivityType.CargoCompleted, input.CargoCompleted);

        return new PortCallPerformanceMetrics(
            Measure(arrival, departure, [arrival, departure]),
            Measure(arrival, allFast, [arrival, allFast, departure]),
            Measure(cargoStarted, cargoCompleted, [arrival, cargoStarted, cargoCompleted, departure]),
            Measure(cargoCompleted, departure, [arrival, cargoCompleted, departure]));
    }

    private static MilestonePoint Anchor(string name, DateTime? occurredAtUtc) =>
        occurredAtUtc is { } at
            ? new MilestonePoint(name, at, null)
            : new MilestonePoint(name, default, PortCallMetricQuality.Missing);

    private static MilestonePoint Single(string name, PortCallMilestoneOccurrences occurrences) =>
        occurrences switch
        {
            { Count: 1, EarliestAtUtc: { } at } => new MilestonePoint(name, at, null),
            { Count: > 1 } => new MilestonePoint(name, default, PortCallMetricQuality.Ambiguous),
            _ => new MilestonePoint(name, default, PortCallMetricQuality.Missing),
        };

    private static PortCallMetric Measure(
        MilestonePoint from,
        MilestonePoint to,
        IReadOnlyList<MilestonePoint> chronology)
    {
        foreach (var point in chronology)
        {
            if (point.UnavailableQuality is { } quality)
            {
                return PortCallMetric.Unavailable(quality, point.Name);
            }
        }

        for (var index = 1; index < chronology.Count; index++)
        {
            if (chronology[index].AtUtc < chronology[index - 1].AtUtc)
            {
                return PortCallMetric.InvalidChronology();
            }
        }

        return PortCallMetric.Measured((int)((to.AtUtc - from.AtUtc).Ticks / TimeSpan.TicksPerMinute));
    }

    private readonly record struct MilestonePoint(string Name, DateTime AtUtc, string? UnavailableQuality);
}
