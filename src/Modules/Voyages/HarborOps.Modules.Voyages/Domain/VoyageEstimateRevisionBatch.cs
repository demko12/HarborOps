namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageEstimateRevisionBatch
{
    internal const int ReasonMaxLength = 200;

    private VoyageEstimateRevisionBatch()
    {
    }

    private VoyageEstimateRevisionBatch(
        Guid id,
        Guid voyageId,
        Guid delayId,
        string reason,
        Guid revisedByUserId,
        DateTime revisedAtUtc)
    {
        Id = id;
        VoyageId = voyageId;
        DelayId = delayId;
        Reason = reason;
        RevisedByUserId = revisedByUserId;
        RevisedAtUtc = revisedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid VoyageId { get; private set; }

    public Guid DelayId { get; private set; }

    public string Reason { get; private set; } = null!;

    public Guid RevisedByUserId { get; private set; }

    public DateTime RevisedAtUtc { get; private set; }

    internal static VoyageEstimateRevisionBatch Record(
        Guid voyageId,
        Guid delayId,
        string reason,
        Guid revisedByUserId,
        DateTime revisedAtUtc)
    {
        return new VoyageEstimateRevisionBatch(
            Guid.NewGuid(),
            voyageId,
            delayId,
            reason,
            revisedByUserId,
            revisedAtUtc);
    }
}
