namespace HarborOps.Modules.Compliance.Contracts;

public sealed record RegisterSurveyAuditRequest(
    Guid? VesselId,
    DateOnly? SurveyDate,
    string? Type,
    string? InspectorName,
    string? Result,
    string? Findings,
    Guid? DocumentId);
