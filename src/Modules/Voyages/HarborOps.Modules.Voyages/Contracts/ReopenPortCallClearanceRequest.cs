namespace HarborOps.Modules.Voyages.Contracts;

public sealed record ReopenPortCallClearanceRequest
{
    public string? Reason { get; init; }
}
