namespace HarborOps.Modules.Compliance.Contracts;

public sealed record SurveyAuditListRequest(
    Guid? VesselId,
    string? Type,
    string? Result,
    DateOnly? FromDate,
    DateOnly? ToDate,
    string? Search,
    int PageNumber = 1,
    int PageSize = 25);
