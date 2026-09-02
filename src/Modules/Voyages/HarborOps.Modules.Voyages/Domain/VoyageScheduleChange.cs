namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageScheduleChange
{
    internal const int ChangeTypeMaxLength = 30;
    internal const int ReasonMaxLength = 200;
    internal const string AmendedChangeType = "Amended";
    internal const string CancelledChangeType = "Cancelled";

    private VoyageScheduleChange()
    {
    }

    private VoyageScheduleChange(
        Guid id,
        Guid voyageId,
        string changeType,
        string reason,
        Guid changedByUserId,
        DateTime changedAtUtc,
        string previousStatus,
        string newStatus,
        Guid? previousRouteId,
        Guid? newRouteId,
        Guid? previousDeparturePortId,
        Guid? newDeparturePortId,
        Guid? previousArrivalPortId,
        Guid? newArrivalPortId,
        DateTime previousPlannedDeparture,
        DateTime newPlannedDeparture,
        DateTime previousPlannedArrival,
        DateTime newPlannedArrival)
    {
        Id = id;
        VoyageId = voyageId;
        ChangeType = changeType;
        Reason = reason;
        ChangedByUserId = changedByUserId;
        ChangedAtUtc = changedAtUtc;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        PreviousRouteId = previousRouteId;
        NewRouteId = newRouteId;
        PreviousDeparturePortId = previousDeparturePortId;
        NewDeparturePortId = newDeparturePortId;
        PreviousArrivalPortId = previousArrivalPortId;
        NewArrivalPortId = newArrivalPortId;
        PreviousPlannedDeparture = previousPlannedDeparture;
        NewPlannedDeparture = newPlannedDeparture;
        PreviousPlannedArrival = previousPlannedArrival;
        NewPlannedArrival = newPlannedArrival;
    }

    public Guid Id { get; private set; }

    public Guid VoyageId { get; private set; }

    public string ChangeType { get; private set; } = null!;

    public string Reason { get; private set; } = null!;

    public Guid ChangedByUserId { get; private set; }

    public DateTime ChangedAtUtc { get; private set; }

    public string PreviousStatus { get; private set; } = null!;

    public string NewStatus { get; private set; } = null!;

    public Guid? PreviousRouteId { get; private set; }

    public Guid? NewRouteId { get; private set; }

    public Guid? PreviousDeparturePortId { get; private set; }

    public Guid? NewDeparturePortId { get; private set; }

    public Guid? PreviousArrivalPortId { get; private set; }

    public Guid? NewArrivalPortId { get; private set; }

    public DateTime PreviousPlannedDeparture { get; private set; }

    public DateTime NewPlannedDeparture { get; private set; }

    public DateTime PreviousPlannedArrival { get; private set; }

    public DateTime NewPlannedArrival { get; private set; }

    internal static VoyageScheduleChange RecordAmendment(
        Guid voyageId,
        string reason,
        Guid changedByUserId,
        DateTime changedAtUtc,
        string status,
        Guid? previousRouteId,
        Guid? newRouteId,
        Guid? previousDeparturePortId,
        Guid newDeparturePortId,
        Guid? previousArrivalPortId,
        Guid newArrivalPortId,
        DateTime previousPlannedDeparture,
        DateTime newPlannedDeparture,
        DateTime previousPlannedArrival,
        DateTime newPlannedArrival)
    {
        return new VoyageScheduleChange(
            Guid.NewGuid(),
            voyageId,
            AmendedChangeType,
            reason,
            changedByUserId,
            changedAtUtc,
            status,
            status,
            previousRouteId,
            newRouteId,
            previousDeparturePortId,
            newDeparturePortId,
            previousArrivalPortId,
            newArrivalPortId,
            previousPlannedDeparture,
            newPlannedDeparture,
            previousPlannedArrival,
            newPlannedArrival);
    }

    internal static VoyageScheduleChange RecordCancellation(
        Guid voyageId,
        string reason,
        Guid changedByUserId,
        DateTime changedAtUtc,
        string previousStatus,
        string newStatus,
        Guid? routeId,
        Guid? departurePortId,
        Guid? arrivalPortId,
        DateTime plannedDeparture,
        DateTime plannedArrival)
    {
        return new VoyageScheduleChange(
            Guid.NewGuid(),
            voyageId,
            CancelledChangeType,
            reason,
            changedByUserId,
            changedAtUtc,
            previousStatus,
            newStatus,
            routeId,
            routeId,
            departurePortId,
            departurePortId,
            arrivalPortId,
            arrivalPortId,
            plannedDeparture,
            plannedDeparture,
            plannedArrival,
            plannedArrival);
    }
}
