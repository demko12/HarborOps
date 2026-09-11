namespace HarborOps.Modules.Cargo.Contracts;

public sealed record BillOfLadingContainerListItem(
    Guid ContainerId,
    string ContainerNumber,
    string ContainerType,
    bool IsSnapshotted);
