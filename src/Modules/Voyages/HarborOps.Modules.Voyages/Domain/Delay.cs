namespace HarborOps.Modules.Voyages.Domain;

internal sealed class Delay
{
    internal const int ReasonMaxLength = 200;

    private Delay()
    {
    }

    private Delay(
        Guid id,
        Guid voyageId,
        string reason,
        int duration,
        DateTime createdAt,
        Guid? voyageStopId,
        string? description)
    {
        Id = id;
        VoyageId = voyageId;
        Reason = reason;
        Duration = duration;
        CreatedAt = createdAt;
        VoyageStopId = voyageStopId;
        Description = description;
    }

    public Guid Id { get; private set; }

    public Guid VoyageId { get; private set; }

    public Guid? VoyageStopId { get; private set; }

    public string Reason { get; private set; } = null!;

    public string? Description { get; private set; }

    public int Duration { get; private set; }

    public DateTime CreatedAt { get; private set; }

    internal static Delay Create(
        Guid voyageId,
        string reason,
        int duration,
        DateTime createdAt,
        Guid? voyageStopId = null,
        string? description = null)
    {
        return new Delay(
            Guid.NewGuid(),
            voyageId,
            reason,
            duration,
            createdAt,
            voyageStopId,
            description);
    }
}
