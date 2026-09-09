namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageEstimateRevisionItem
{
    private VoyageEstimateRevisionItem()
    {
    }

    private VoyageEstimateRevisionItem(
        Guid id,
        Guid revisionBatchId,
        Guid voyageStopId,
        DateTime previousEta,
        DateTime previousEtd,
        DateTime newEta,
        DateTime newEtd)
    {
        Id = id;
        RevisionBatchId = revisionBatchId;
        VoyageStopId = voyageStopId;
        PreviousEta = previousEta;
        PreviousEtd = previousEtd;
        NewEta = newEta;
        NewEtd = newEtd;
    }

    public Guid Id { get; private set; }

    public Guid RevisionBatchId { get; private set; }

    public Guid VoyageStopId { get; private set; }

    public DateTime PreviousEta { get; private set; }

    public DateTime PreviousEtd { get; private set; }

    public DateTime NewEta { get; private set; }

    public DateTime NewEtd { get; private set; }

    internal static VoyageEstimateRevisionItem Record(
        Guid revisionBatchId,
        Guid voyageStopId,
        DateTime previousEta,
        DateTime previousEtd,
        DateTime newEta,
        DateTime newEtd)
    {
        return new VoyageEstimateRevisionItem(
            Guid.NewGuid(),
            revisionBatchId,
            voyageStopId,
            previousEta,
            previousEtd,
            newEta,
            newEtd);
    }
}
