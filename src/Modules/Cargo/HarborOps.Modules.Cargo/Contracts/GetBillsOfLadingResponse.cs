namespace HarborOps.Modules.Cargo.Contracts;

public sealed record GetBillsOfLadingResponse(
    Guid CargoBookingId,
    string BookingNumber,
    string BookingStatus,
    IReadOnlyList<BillOfLadingListItem> BillsOfLading);
