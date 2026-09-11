namespace HarborOps.Modules.Voyages.Contracts;

public sealed record WaivePortCallClearanceItemRequest
{
    public string? Reason { get; init; }
}
