namespace HarborOps.Modules.Cargo.Contracts;

public sealed record GetContainerControlResponse(
    ContainerControlDetails Control,
    IReadOnlyList<ContainerControlEventListItem> History);
