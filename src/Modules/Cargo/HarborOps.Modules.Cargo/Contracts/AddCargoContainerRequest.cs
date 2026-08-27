namespace HarborOps.Modules.Cargo.Contracts;

public sealed record AddCargoContainerRequest
{
    public string? ContainerNumber { get; init; }

    public string? ContainerType { get; init; }
}
