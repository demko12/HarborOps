namespace HarborOps.Modules.Cargo.Contracts;

public sealed record BillOfLadingListItem(
    Guid BillOfLadingId,
    string BillNumber,
    string Status,
    string ShipperName,
    string ConsigneeName,
    string PortOfLoading,
    string PortOfDischarge,
    int ContainerCount,
    Guid? IssuedByUserId,
    DateTime? IssuedAtUtc,
    DateTime? VoidedAtUtc,
    DateTime CreatedUtc);
