namespace HarborOps.Modules.Voyages.Contracts;

public sealed record AmendVoyageScheduleRequest
{
    public Guid? RouteId { get; init; }

    public Guid? DeparturePortId { get; init; }

    public Guid? ArrivalPortId { get; init; }

    public DateTime? PlannedDepartureUtc { get; init; }

    public DateTime? PlannedArrivalUtc { get; init; }

    public string? Reason { get; init; }
}
