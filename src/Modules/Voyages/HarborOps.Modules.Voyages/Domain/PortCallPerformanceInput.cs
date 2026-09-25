namespace HarborOps.Modules.Voyages.Domain;

internal sealed record PortCallPerformanceInput(
    DateTime? ActualArrivalUtc,
    DateTime? ActualDepartureUtc,
    PortCallMilestoneOccurrences AllFast,
    PortCallMilestoneOccurrences CargoStarted,
    PortCallMilestoneOccurrences CargoCompleted);
