namespace HarborOps.Modules.Voyages.Domain;

internal sealed class BerthAllocationChange
{
    internal const int ReasonMaxLength = 200;

    private BerthAllocationChange()
    {
    }

    private BerthAllocationChange(
        Guid voyageId,
        Guid voyageStopId,
        Guid? previousBerthId,
        Guid newBerthId,
        DateTime previousPlannedArrivalUtc,
        DateTime previousPlannedDepartureUtc,
        DateTime newPlannedArrivalUtc,
        DateTime newPlannedDepartureUtc,
        string reason,
        Guid changedByUserId,
        DateTime changedAtUtc)
    {
        Id = Guid.NewGuid();
        VoyageId = voyageId;
        VoyageStopId = voyageStopId;
        PreviousBerthId = previousBerthId;
        NewBerthId = newBerthId;
        PreviousPlannedArrivalUtc = previousPlannedArrivalUtc;
        PreviousPlannedDepartureUtc = previousPlannedDepartureUtc;
        NewPlannedArrivalUtc = newPlannedArrivalUtc;
        NewPlannedDepartureUtc = newPlannedDepartureUtc;
        Reason = reason;
        ChangedByUserId = changedByUserId;
        ChangedAtUtc = changedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid VoyageId { get; private set; }
    public Guid VoyageStopId { get; private set; }
    public Guid? PreviousBerthId { get; private set; }
    public Guid NewBerthId { get; private set; }
    public DateTime PreviousPlannedArrivalUtc { get; private set; }
    public DateTime PreviousPlannedDepartureUtc { get; private set; }
    public DateTime NewPlannedArrivalUtc { get; private set; }
    public DateTime NewPlannedDepartureUtc { get; private set; }
    public string Reason { get; private set; } = null!;
    public Guid ChangedByUserId { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }

    internal static BerthAllocationChange Record(
        Guid voyageId, Guid voyageStopId, Guid? previousBerthId, Guid newBerthId,
        DateTime previousPlannedArrivalUtc, DateTime previousPlannedDepartureUtc,
        DateTime newPlannedArrivalUtc, DateTime newPlannedDepartureUtc,
        string reason, Guid changedByUserId, DateTime changedAtUtc)
        => new(voyageId, voyageStopId, previousBerthId, newBerthId,
            previousPlannedArrivalUtc, previousPlannedDepartureUtc,
            newPlannedArrivalUtc, newPlannedDepartureUtc, reason, changedByUserId, changedAtUtc);
}
