namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageLeg
{
    private VoyageLeg()
    {
    }

    private VoyageLeg(
        Guid id,
        Guid voyageId,
        Guid fromStopId,
        Guid toStopId,
        decimal distance,
        int estimatedDuration)
    {
        Id = id;
        VoyageId = voyageId;
        FromStopId = fromStopId;
        ToStopId = toStopId;
        Distance = distance;
        EstimatedDuration = estimatedDuration;
    }

    public Guid Id { get; private set; }

    public Guid VoyageId { get; private set; }

    public Guid FromStopId { get; private set; }

    public Guid ToStopId { get; private set; }

    public decimal Distance { get; private set; }

    public int EstimatedDuration { get; private set; }

    internal static VoyageLeg Create(
        Guid voyageId,
        Guid fromStopId,
        Guid toStopId,
        decimal distance,
        int estimatedDuration)
    {
        return new VoyageLeg(
            Guid.NewGuid(),
            voyageId,
            fromStopId,
            toStopId,
            distance,
            estimatedDuration);
    }
}
