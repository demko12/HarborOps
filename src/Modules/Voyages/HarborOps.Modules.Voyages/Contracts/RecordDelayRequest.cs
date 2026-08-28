namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RecordDelayRequest
{
    public string? Reason { get; init; }

    public string? Description { get; init; }

    public int? Duration { get; init; }

    public Guid? VoyageStopId { get; init; }
}
