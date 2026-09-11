namespace HarborOps.Modules.Cargo.Domain;

internal sealed record BillOfLadingDraftValues(
    string BillNumber,
    string ShipperName,
    string ShipperAddress,
    string ConsigneeName,
    string ConsigneeAddress,
    string? NotifyPartyName,
    string? NotifyPartyAddress,
    string PlaceOfReceipt,
    string PortOfLoading,
    string PortOfDischarge,
    string PlaceOfDelivery,
    FreightTerms FreightTerms,
    string CargoMarks,
    string CargoDescription,
    int PackageCount,
    decimal WeightKg,
    string IssuePlace,
    DateOnly RequestedIssueDate);
