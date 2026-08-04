namespace HarborOps.Modules.Voyages.Domain;

internal sealed class Voyage
{
    internal const int StatusMaxLength = 30;
    internal const int VoyageNumberMaxLength = 50;

    private Voyage()
    {
    }

    private Voyage(
        Guid id,
        Guid vesselId,
        string voyageNumber,
        string status,
        DateTime plannedDeparture,
        DateTime plannedArrival,
        Guid? routeId,
        Guid? captainId,
        Guid? departurePortId,
        Guid? arrivalPortId,
        string? notes)
    {
        Id = id;
        VesselId = vesselId;
        VoyageNumber = voyageNumber;
        Status = status;
        PlannedDeparture = plannedDeparture;
        PlannedArrival = plannedArrival;
        RouteId = routeId;
        CaptainId = captainId;
        DeparturePortId = departurePortId;
        ArrivalPortId = arrivalPortId;
        Notes = notes;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Scalar identifier of the vessel owned by the Fleet module.
    /// </summary>
    public Guid VesselId { get; private set; }

    public Guid? RouteId { get; private set; }

    /// <summary>
    /// Scalar identifier of the crew member owned by the Crew module.
    /// </summary>
    public Guid? CaptainId { get; private set; }

    public Guid? DeparturePortId { get; private set; }

    public Guid? ArrivalPortId { get; private set; }

    public string VoyageNumber { get; private set; } = null!;

    public string Status { get; private set; } = null!;

    public DateTime PlannedDeparture { get; private set; }

    public DateTime PlannedArrival { get; private set; }

    public DateTime? ActualDeparture { get; private set; }

    public DateTime? ActualArrival { get; private set; }

    public string? Notes { get; private set; }

    internal static Voyage Create(
        Guid vesselId,
        string voyageNumber,
        string status,
        DateTime plannedDeparture,
        DateTime plannedArrival,
        Guid? routeId = null,
        Guid? captainId = null,
        Guid? departurePortId = null,
        Guid? arrivalPortId = null,
        string? notes = null)
    {
        return new Voyage(
            Guid.NewGuid(),
            vesselId,
            voyageNumber,
            status,
            plannedDeparture,
            plannedArrival,
            routeId,
            captainId,
            departurePortId,
            arrivalPortId,
            notes);
    }
}
