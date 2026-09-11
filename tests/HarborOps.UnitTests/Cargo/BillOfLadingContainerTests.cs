using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.UnitTests.Cargo;

public sealed class BillOfLadingContainerTests
{
    private static readonly Guid BillOfLadingId =
        Guid.Parse("6f5e4d3c-2b1a-4098-8776-5e4d3c2b1a09");

    private static readonly Guid ContainerId =
        Guid.Parse("0a1b2c3d-4e5f-4061-9273-8495a6b7c8d9");

    [Fact]
    public void Select_links_the_container_without_a_snapshot()
    {
        var selection = BillOfLadingContainer.Select(BillOfLadingId, ContainerId);

        Assert.Equal(BillOfLadingId, selection.BillOfLadingId);
        Assert.Equal(ContainerId, selection.ContainerId);
        Assert.Null(selection.SnapshotContainerNumber);
        Assert.Null(selection.SnapshotContainerType);
        Assert.False(selection.IsSnapshotted);
    }

    [Fact]
    public void CaptureSnapshot_stores_the_number_and_the_type()
    {
        var selection = BillOfLadingContainer.Select(BillOfLadingId, ContainerId);

        selection.CaptureSnapshot("MSKU1234567", "40HC");

        Assert.Equal("MSKU1234567", selection.SnapshotContainerNumber);
        Assert.Equal("40HC", selection.SnapshotContainerType);
        Assert.True(selection.IsSnapshotted);
    }
}
