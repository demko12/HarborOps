namespace HarborOps.Modules.Cargo.Contracts;

public sealed record RecordCargoTrackingEventRequest
{
    public string? Status { get; init; }

    public DateTime? OccurredUtc { get; init; }

    public string? Location { get; init; }
}
