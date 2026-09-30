namespace HarborOps.Modules.Cargo.Contracts;

public sealed record ExportCargoContainersRequest
{
    public string? Search { get; init; }

    public string? Status { get; init; }

    public string? ContainerType { get; init; }

    public string? PortOfLoadingCode { get; init; }

    public string? PortOfDischargeCode { get; init; }
}

public sealed record CargoManifestExportFilters(
    string? Search,
    string? Status,
    string? ContainerType,
    string? PortOfLoadingCode,
    string? PortOfDischargeCode);

public sealed record CargoManifestExportRow(
    Guid ContainerId,
    string ContainerNumber,
    string ContainerType,
    string Status,
    string? PortOfLoadingCode,
    string? PortOfDischargeCode,
    decimal? WeightKg,
    Guid? VesselId);

public sealed record CargoManifestExport(
    Guid CargoBookingId,
    string BookingNumber,
    DateTime GeneratedAtUtc,
    CargoManifestExportFilters Filters,
    bool IsFiltered,
    int MatchingCount,
    int BookingContainerCount,
    decimal KnownWeightKg,
    int KnownWeightCount,
    int UnknownWeightCount,
    int RowLimit,
    IReadOnlyList<CargoManifestExportRow> Rows);
