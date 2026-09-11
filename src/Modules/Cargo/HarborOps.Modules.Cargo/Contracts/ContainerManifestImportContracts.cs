namespace HarborOps.Modules.Cargo.Contracts;

public sealed record ContainerManifestImportRow(int RowNumber, string? ContainerNumber, string? ContainerType, string? PortOfLoadingCode, string? PortOfDischargeCode, decimal? WeightKg, IReadOnlyDictionary<string, string[]> Errors);
public sealed record ContainerManifestPreviewResponse(int TotalCount, int ValidCount, int InvalidCount, IReadOnlyList<ContainerManifestImportRow> Rows);
public sealed record ContainerManifestImportResponse(int CreatedCount, IReadOnlyList<Guid> ContainerIds);
