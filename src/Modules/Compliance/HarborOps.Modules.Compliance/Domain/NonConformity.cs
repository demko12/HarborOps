namespace HarborOps.Modules.Compliance.Domain;

public sealed class NonConformity
{
    internal const int SeverityMaxLength = 50;
    internal const int ClosureStatusMaxLength = 50;

    public int Id { get; private set; }

    public int? SurveyId { get; private set; }

    public SurveyAudit? Survey { get; private set; }

    public int? PscId { get; private set; }

    public PscInspection? Psc { get; private set; }

    public Guid EvidenceDocumentId { get; private set; }

    public string Severity { get; private set; } = string.Empty;

    public DateOnly CorrectiveActionDeadline { get; private set; }

    public string ClosureStatus { get; private set; } = string.Empty;

    private NonConformity()
    {
    }

    public NonConformity(
        Guid evidenceDocumentId,
        string severity,
        DateOnly correctiveActionDeadline,
        string closureStatus,
        int? surveyId = null,
        int? pscId = null)
    {
        EvidenceDocumentId = evidenceDocumentId;
        Severity = severity;
        CorrectiveActionDeadline = correctiveActionDeadline;
        ClosureStatus = closureStatus;
        SurveyId = surveyId;
        PscId = pscId;
    }
}
