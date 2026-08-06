namespace HarborOps.Modules.Voyages.Contracts;

public sealed record ScheduleVoyageRequest
{
    public string? VoyageNumber { get; init; }

    public Guid? VesselId { get; init; }

    public string? DeparturePortCode { get; init; }

    public string? ArrivalPortCode { get; init; }

    public DateTime? PlannedDepartureUtc { get; init; }

    public DateTime? PlannedArrivalUtc { get; init; }
}
