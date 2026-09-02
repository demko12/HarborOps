namespace HarborOps.Modules.Voyages.Contracts;

public sealed record CancelVoyageRequest
{
    public string? Reason { get; init; }
}
