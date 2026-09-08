namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageStop
{
    internal const int StatusMaxLength = 30;

    internal const string PlannedStatus = "Planned";
    internal const string ArrivedStatus = "Arrived";
    internal const string DepartedStatus = "Departed";

    private VoyageStop()
    {
    }

    private VoyageStop(
        Guid id,
        Guid voyageId,
        Guid portId,
        int stopOrder,
        DateTime eta,
        DateTime etd,
        string status,
        Guid? berthId)
    {
        Id = id;
        VoyageId = voyageId;
        PortId = portId;
        StopOrder = stopOrder;
        Eta = eta;
        Etd = etd;
        Status = status;
        BerthId = berthId;
    }

    public Guid Id { get; private set; }

    public Guid VoyageId { get; private set; }

    public Guid PortId { get; private set; }

    public Guid? BerthId { get; private set; }

    public int StopOrder { get; private set; }

    public DateTime Eta { get; private set; }

    public DateTime Etd { get; private set; }

    public DateTime? RevisedEta { get; private set; }

    public DateTime? RevisedEtd { get; private set; }

    public DateTime? Ata { get; private set; }

    public DateTime? Atd { get; private set; }

    public string Status { get; private set; } = null!;

    internal DateTime CurrentEta => RevisedEta ?? Eta;

    internal DateTime CurrentEtd => RevisedEtd ?? Etd;

    internal static VoyageStop Create(
        Guid voyageId,
        Guid portId,
        int stopOrder,
        DateTime eta,
        DateTime etd)
    {
        return new VoyageStop(
            Guid.NewGuid(),
            voyageId,
            portId,
            stopOrder,
            eta,
            etd,
            PlannedStatus,
            null);
    }

    internal void AssignBerth(Guid berthId)
    {
        if (!string.Equals(Status, PlannedStatus, StringComparison.Ordinal))
        {
            throw new VoyageStopNotPlannedException(Id, Status);
        }

        BerthId = berthId;
    }

    internal void RecordArrival(DateTime ataUtc)
    {
        if (!string.Equals(Status, PlannedStatus, StringComparison.Ordinal) || Ata is not null)
        {
            throw new VoyageStopNotPlannedException(Id, Status);
        }

        Ata = ataUtc;
        Status = ArrivedStatus;
    }

    internal void RecordDeparture(DateTime atdUtc)
    {
        if (!string.Equals(Status, ArrivedStatus, StringComparison.Ordinal)
            || Ata is null
            || Atd is not null)
        {
            throw new VoyageStopNotArrivedException(Id, Status);
        }

        Atd = atdUtc;
        Status = DepartedStatus;
    }
}
