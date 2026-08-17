namespace HarborOps.Modules.Compliance.Contracts;

public sealed record RegisterNonConformityRequest
{
    public int? SurveyId { get; init; }

    public int? PscId { get; init; }

    public Guid? EvidenceDocumentId { get; init; }

    public string? Severity { get; init; }

    public DateOnly? CorrectiveActionDeadline { get; init; }
}
