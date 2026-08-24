namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RegisterBerthRequest
{
    public Guid? PortId { get; init; }

    public string? Name { get; init; }

    public decimal? Length { get; init; }

    public decimal? MaxDraft { get; init; }

    public string? Status { get; init; }
}
