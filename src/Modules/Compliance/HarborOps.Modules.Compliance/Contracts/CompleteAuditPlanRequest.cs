namespace HarborOps.Modules.Compliance.Contracts;

public sealed record CompleteAuditPlanRequest
{
    public string? Result { get; init; }

    public string? Findings { get; init; }

    public Guid? DocumentId { get; init; }

    public DateOnly? ActualAuditDate { get; init; }
}

public sealed record CompleteAuditPlanResponse(
    Guid Id,
    int SurveyAuditId,
    DateTime CompletedAtUtc);
