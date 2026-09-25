namespace HarborOps.Modules.Voyages.Domain;

internal sealed record PortCallPerformanceMetrics(
    PortCallMetric Turnaround,
    PortCallMetric ArrivalToAllFast,
    PortCallMetric CargoElapsed,
    PortCallMetric CargoCompletedToDeparture);
