namespace HarborOps.Modules.Cargo.Domain;

internal sealed class BillOfLadingContainer
{
    private BillOfLadingContainer()
    {
    }

    private BillOfLadingContainer(Guid id, Guid billOfLadingId, Guid containerId)
    {
        Id = id;
        BillOfLadingId = billOfLadingId;
        ContainerId = containerId;
        IsActiveSelection = true;
    }

    public Guid Id { get; private set; }

    public Guid BillOfLadingId { get; private set; }

    public Guid ContainerId { get; private set; }

    public bool IsActiveSelection { get; private set; }

    public string? SnapshotContainerNumber { get; private set; }

    public string? SnapshotContainerType { get; private set; }

    internal bool IsSnapshotted => SnapshotContainerNumber is not null;

    internal static BillOfLadingContainer Select(Guid billOfLadingId, Guid containerId)
    {
        return new BillOfLadingContainer(Guid.NewGuid(), billOfLadingId, containerId);
    }

    internal void CaptureSnapshot(string containerNumber, string containerType)
    {
        SnapshotContainerNumber = containerNumber;
        SnapshotContainerType = containerType;
    }

    internal void ReleaseSelection()
    {
        IsActiveSelection = false;
    }
}
