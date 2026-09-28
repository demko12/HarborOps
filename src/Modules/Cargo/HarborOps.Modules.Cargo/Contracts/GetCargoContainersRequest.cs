namespace HarborOps.Modules.Cargo.Contracts;

public sealed record GetCargoContainersRequest
{
    public string? Search { get; init; }

    public string? Status { get; init; }

    public string? ContainerType { get; init; }

    public string? PortOfLoadingCode { get; init; }

    public string? PortOfDischargeCode { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
