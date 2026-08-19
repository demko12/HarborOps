namespace HarborOps.Modules.Compliance.Contracts;

public sealed record SurveyAuditOverviewItem(
    int Id,
    Guid VesselId,
    DateOnly Date,
    string Type,
    string Result);
