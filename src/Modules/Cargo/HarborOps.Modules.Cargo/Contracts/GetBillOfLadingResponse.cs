namespace HarborOps.Modules.Cargo.Contracts;

public sealed record GetBillOfLadingResponse(
    BillOfLadingDetails BillOfLading,
    IReadOnlyList<BillOfLadingEventListItem> History);
