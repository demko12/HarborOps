namespace HarborOps.Modules.Compliance.Contracts;

public sealed record GetNonConformitiesRequest
{
    public string? Search { get; init; }

    public string? Severity { get; init; }

    public string? ClosureStatus { get; init; }

    public bool? Overdue { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

public sealed record NonConformityListItem(
    int Id,
    Guid VesselId,
    int? SurveyId,
    int? PscId,
    string Severity,
    string ClosureStatus,
    DateOnly CorrectiveActionDeadline,
    bool IsOverdue,
    Guid EvidenceDocumentId,
    int CorrectiveActionCount);

public sealed record GetNonConformitiesResponse(
    IReadOnlyList<NonConformityListItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);
