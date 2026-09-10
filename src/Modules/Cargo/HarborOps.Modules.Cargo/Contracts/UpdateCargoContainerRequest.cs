namespace HarborOps.Modules.Cargo.Contracts;

public sealed record UpdateCargoContainerRequest
{
    public string? ContainerNumber { get; init; }
    public string? ContainerType { get; init; }
    public string? PortOfLoadingCode { get; init; }
    public string? PortOfDischargeCode { get; init; }
    public decimal? WeightKg { get; init; }
}

public sealed record UpdateCargoContainerResponse(Guid ContainerId, string ContainerNumber, string ContainerType, string PortOfLoadingCode, string PortOfDischargeCode, decimal WeightKg);
