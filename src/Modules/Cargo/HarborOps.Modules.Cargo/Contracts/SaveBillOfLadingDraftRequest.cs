namespace HarborOps.Modules.Cargo.Contracts;

public sealed record SaveBillOfLadingDraftRequest
{
    public string? BillNumber { get; init; }

    public string? ShipperName { get; init; }

    public string? ShipperAddress { get; init; }

    public string? ConsigneeName { get; init; }

    public string? ConsigneeAddress { get; init; }

    public string? NotifyPartyName { get; init; }

    public string? NotifyPartyAddress { get; init; }

    public string? PlaceOfReceipt { get; init; }

    public string? PortOfLoading { get; init; }

    public string? PortOfDischarge { get; init; }

    public string? PlaceOfDelivery { get; init; }

    public string? FreightTerms { get; init; }

    public string? CargoMarks { get; init; }

    public string? CargoDescription { get; init; }

    public int? PackageCount { get; init; }

    public decimal? WeightKg { get; init; }

    public string? IssuePlace { get; init; }

    public DateOnly? RequestedIssueDate { get; init; }

    public IReadOnlyList<Guid>? ContainerIds { get; init; }
}
