namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record RejectDefectRequest
{
    public string? Reason { get; init; }
}
