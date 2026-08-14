namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetDocumentTypesRequest
{
    public string? Search { get; init; }

    public int? PageSize { get; init; }
}
