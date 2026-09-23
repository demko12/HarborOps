namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferProfileChange
{
    private ReeferProfileChange()
    {
    }

    private ReeferProfileChange(
        Guid id,
        Guid reeferMonitoringProfileId,
        int version,
        decimal? fromMinimumCelsius,
        decimal? fromMaximumCelsius,
        decimal? fromTargetCelsius,
        decimal toMinimumCelsius,
        decimal toMaximumCelsius,
        decimal toTargetCelsius,
        string? reason,
        Guid actorUserId,
        DateTime occurredUtc)
    {
        Id = id;
        ReeferMonitoringProfileId = reeferMonitoringProfileId;
        Version = version;
        FromMinimumCelsius = fromMinimumCelsius;
        FromMaximumCelsius = fromMaximumCelsius;
        FromTargetCelsius = fromTargetCelsius;
        ToMinimumCelsius = toMinimumCelsius;
        ToMaximumCelsius = toMaximumCelsius;
        ToTargetCelsius = toTargetCelsius;
        Reason = reason;
        ActorUserId = actorUserId;
        OccurredUtc = occurredUtc;
    }

    public Guid Id { get; private set; }

    public Guid ReeferMonitoringProfileId { get; private set; }

    public int Version { get; private set; }

    public decimal? FromMinimumCelsius { get; private set; }

    public decimal? FromMaximumCelsius { get; private set; }

    public decimal? FromTargetCelsius { get; private set; }

    public decimal ToMinimumCelsius { get; private set; }

    public decimal ToMaximumCelsius { get; private set; }

    public decimal ToTargetCelsius { get; private set; }

    public string? Reason { get; private set; }

    public Guid ActorUserId { get; private set; }

    public DateTime OccurredUtc { get; private set; }

    internal static ReeferProfileChange Record(
        Guid reeferMonitoringProfileId,
        int version,
        decimal? fromMinimumCelsius,
        decimal? fromMaximumCelsius,
        decimal? fromTargetCelsius,
        decimal toMinimumCelsius,
        decimal toMaximumCelsius,
        decimal toTargetCelsius,
        string? reason,
        Guid actorUserId,
        DateTime occurredUtc)
    {
        return new ReeferProfileChange(
            Guid.NewGuid(),
            reeferMonitoringProfileId,
            version,
            fromMinimumCelsius,
            fromMaximumCelsius,
            fromTargetCelsius,
            toMinimumCelsius,
            toMaximumCelsius,
            toTargetCelsius,
            reason,
            actorUserId,
            occurredUtc);
    }
}
