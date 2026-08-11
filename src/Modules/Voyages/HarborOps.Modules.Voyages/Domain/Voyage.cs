namespace HarborOps.Modules.Voyages.Domain;

internal sealed class Voyage
{
    internal const int PortCodeLength = 5;
    internal const int StatusMaxLength = 30;
    internal const int VoyageNumberMaxLength = 50;
    internal const string PlannedStatus = "Planned";
    internal const string InProgressStatus = "InProgress";
    internal const string CompletedStatus = "Completed";

    private Voyage()
    {
    }

    private Voyage(
        Guid id,
        Guid vesselId,
        string voyageNumber,
        string departurePortCode,
        string arrivalPortCode,
        string status,
        DateTime plannedDeparture,
        DateTime plannedArrival,
        DateTime createdUtc)
    {
        Id = id;
        VesselId = vesselId;
        VoyageNumber = voyageNumber;
        DeparturePortCode = departurePortCode;
        ArrivalPortCode = arrivalPortCode;
        Status = status;
        PlannedDeparture = plannedDeparture;
        PlannedArrival = plannedArrival;
        CreatedUtc = createdUtc;
    }

    public Guid Id { get; private set; }
    public Guid VesselId { get; private set; }

    public Guid? RouteId { get; private set; }
    public Guid? CaptainId { get; private set; }

    public Guid? DeparturePortId { get; private set; }

    public Guid? ArrivalPortId { get; private set; }

    public string VoyageNumber { get; private set; } = null!;

    public string DeparturePortCode { get; private set; } = null!;

    public string ArrivalPortCode { get; private set; } = null!;

    public string Status { get; private set; } = null!;

    public DateTime PlannedDeparture { get; private set; }

    public DateTime PlannedArrival { get; private set; }

    public DateTime? ActualDeparture { get; private set; }

    public DateTime? ActualArrival { get; private set; }

    public string? Notes { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    internal static Voyage Schedule(
        Guid vesselId,
        string voyageNumber,
        string departurePortCode,
        string arrivalPortCode,
        DateTime plannedDeparture,
        DateTime plannedArrival)
    {
        return new Voyage(
            Guid.NewGuid(),
            vesselId,
            voyageNumber,
            departurePortCode,
            arrivalPortCode,
            PlannedStatus,
            plannedDeparture,
            plannedArrival,
            DateTime.UtcNow);
    }

    internal void Depart(DateTime actualDepartureUtc)
    {
        if (!string.Equals(Status, PlannedStatus, StringComparison.Ordinal)
            || ActualDeparture is not null)
        {
            throw new VoyageNotPlannedException(Id, Status);
        }

        ActualDeparture = actualDepartureUtc;
        Status = InProgressStatus;
    }

    internal void Arrive(DateTime actualArrivalUtc)
    {
        if (!string.Equals(Status, InProgressStatus, StringComparison.Ordinal)
            || ActualDeparture is null
            || ActualArrival is not null)
        {
            throw new VoyageNotInProgressException(Id, Status);
        }

        ActualArrival = actualArrivalUtc;
        Status = CompletedStatus;
    }
}
