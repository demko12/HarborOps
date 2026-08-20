namespace HarborOps.Modules.Compliance.Contracts;

public sealed record SurveyAuditListResponse(IReadOnlyList<SurveyAuditListItem> Items, int TotalCount, int PageNumber, int PageSize);
public sealed record SurveyAuditListItem(int Id, Guid VesselId, DateOnly SurveyDate, string Type, string? InspectorName, string Result, Guid? DocumentId);
public sealed record SurveyAuditDetails(int Id, Guid VesselId, DateOnly SurveyDate, string Type, string? InspectorName, string Result, string Findings, Guid? DocumentId);
