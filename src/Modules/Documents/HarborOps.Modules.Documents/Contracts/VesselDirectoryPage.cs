namespace HarborOps.Modules.Documents.Contracts;

public sealed record VesselDirectoryPage(IReadOnlyList<VesselExistenceResult> Items, int TotalCount);
