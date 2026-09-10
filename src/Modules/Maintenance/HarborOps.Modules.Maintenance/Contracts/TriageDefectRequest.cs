namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record TriageDefectRequest
{
    public string? ConfirmedSeverity { get; init; }

    public string? Priority { get; init; }

    public string? Notes { get; init; }
}
