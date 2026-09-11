namespace HarborOps.Modules.Cargo.Contracts;

public sealed record AddCargoContainerRequest
{
    public string? ContainerNumber { get; init; }

    public string? ContainerType { get; init; }

    public string? PortOfLoadingCode { get; init; }

    public string? PortOfDischargeCode { get; init; }

    public decimal? WeightKg { get; init; }
}
