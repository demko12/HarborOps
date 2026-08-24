namespace HarborOps.Modules.Voyages.Contracts;

public sealed record SearchBerthsRequest
{
    public Guid? PortId { get; init; }

    public string? Search { get; init; }

    public string? Status { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
