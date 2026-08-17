namespace HarborOps.Web.Api.Documents;

internal sealed record GetExpiringDocumentsQuery(
    DateOnly? From,
    DateOnly? To,
    Guid? VesselId,
    int? PageNumber,
    int? PageSize);

internal sealed record ExpiringDocumentListItem(
    Guid DocumentId,
    Guid VesselId,
    string Number,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    string IssuingAuthority,
    string Status,
    string DocumentTypeName,
    int DaysUntilExpiry);

internal sealed record ExpiringDocumentsPage(
    IReadOnlyList<ExpiringDocumentListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record GetDocumentsQuery(Guid? VesselId, string? Search, int? PageNumber, int? PageSize);

internal sealed record DocumentListItem(
    Guid DocumentId,
    Guid VesselId,
    string Number,
    string DocumentTypeName,
    string Status,
    DateOnly ExpiryDate);

internal sealed record DocumentsPage(
    IReadOnlyList<DocumentListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record RegisterDocumentRequest
{
    public Guid? VesselId { get; init; }

    public int? DocumentTypeId { get; init; }

    public string? Number { get; init; }

    public DateOnly? IssueDate { get; init; }

    public DateOnly? ExpiryDate { get; init; }

    public string? IssuingAuthority { get; init; }

    public string? Status { get; init; }
}

internal sealed record RegisterDocumentResult(Guid Id);

internal sealed record GetDocumentTypesQuery(string? Search, int? PageSize);

internal sealed record DocumentTypeListItem(
    int DocumentTypeId,
    string Name,
    int MandatoryValidityPeriodDays,
    string IssuingAuthority,
    bool RequiresSurvey);

internal sealed record DocumentTypesPage(
    IReadOnlyList<DocumentTypeListItem> Items,
    int PageSize,
    int TotalCount);

internal sealed record RegisterDocumentTypeRequest
{
    public string? Name { get; init; }

    public int? MandatoryValidityPeriodDays { get; init; }

    public string? IssuingAuthority { get; init; }

    public bool? RequiresSurvey { get; init; }
}

internal sealed record RegisterDocumentTypeResult(int Id);

internal sealed record DocumentsApiResult<T>(T? Value, DocumentsApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record DocumentsApiFailure(
    int StatusCode,
    string Title,
    string Message,
    IReadOnlyDictionary<string, string[]> Errors)
{
    internal bool IsValidation => StatusCode == StatusCodes.Status400BadRequest;

    internal bool IsForbidden => StatusCode == StatusCodes.Status403Forbidden;

    internal bool IsConflict => StatusCode == StatusCodes.Status409Conflict;

    internal bool IsNotFound => StatusCode == StatusCodes.Status404NotFound;
}
