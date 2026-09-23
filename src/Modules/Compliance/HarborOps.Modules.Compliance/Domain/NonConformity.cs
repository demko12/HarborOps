namespace HarborOps.Modules.Compliance.Domain;

public sealed class NonConformity
{
    internal const int SeverityMaxLength = 50;
    internal const int ClosureStatusMaxLength = 50;
    internal const int ClosureNotesMaxLength = 1000;
    internal const int PscDeficiencyCodeMaxLength = 50;
    internal const int PscDeficiencyDescriptionMaxLength = 2000;

    internal const string SeverityMinor = "Minor";
    internal const string SeverityMajor = "Major";
    internal const string SeverityCritical = "Critical";
    internal const string ClosureStatusOpen = "Open";
    internal const string ClosureStatusInProgress = "InProgress";
    internal const string ClosureStatusClosed = "Closed";

    private readonly List<CorrectiveAction> correctiveActions = [];

    public int Id { get; private set; }

    public int? SurveyId { get; private set; }

    public SurveyAudit? Survey { get; private set; }

    public int? PscId { get; private set; }

    public PscInspection? Psc { get; private set; }

    public Guid EvidenceDocumentId { get; private set; }

    public string Severity { get; private set; } = string.Empty;

    public DateOnly CorrectiveActionDeadline { get; private set; }

    public string ClosureStatus { get; private set; } = string.Empty;

    public string? ClosureNotes { get; private set; }

    public string? PscDeficiencyCode { get; private set; }

    public string? PscDeficiencyDescription { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public Guid? ClosedByUserId { get; private set; }

    public Guid? FinalEvidenceDocumentId { get; private set; }

    public IReadOnlyCollection<CorrectiveAction> CorrectiveActions => correctiveActions;

    private NonConformity()
    {
    }

    public NonConformity(
        Guid evidenceDocumentId,
        string severity,
        DateOnly correctiveActionDeadline,
        string closureStatus,
        int? surveyId = null,
        int? pscId = null,
        string? pscDeficiencyCode = null,
        string? pscDeficiencyDescription = null)
    {
        EvidenceDocumentId = evidenceDocumentId;
        Severity = severity;
        CorrectiveActionDeadline = correctiveActionDeadline;
        ClosureStatus = closureStatus;
        SurveyId = surveyId;
        PscId = pscId;
        PscDeficiencyCode = pscDeficiencyCode;
        PscDeficiencyDescription = pscDeficiencyDescription;
    }

    internal static NonConformity Register(
        Guid evidenceDocumentId,
        string severity,
        DateOnly correctiveActionDeadline,
        int? surveyId = null,
        int? pscId = null,
        string? pscDeficiencyCode = null,
        string? pscDeficiencyDescription = null)
    {
        return new NonConformity(
            evidenceDocumentId,
            severity,
            correctiveActionDeadline,
            ClosureStatusOpen,
            surveyId,
            pscId,
            pscDeficiencyCode,
            pscDeficiencyDescription);
    }

    internal CorrectiveAction AddCorrectiveAction(
        string actionText,
        Guid actorUserId,
        DateTimeOffset recordedAtUtc,
        Guid? evidenceDocumentId)
    {
        if (ClosureStatus == ClosureStatusClosed)
        {
            throw new InvalidNonConformityTransitionException(Id, ClosureStatus, ClosureStatusInProgress);
        }

        var correctiveAction = CorrectiveAction.Record(
            Id,
            actionText,
            actorUserId,
            recordedAtUtc,
            evidenceDocumentId);

        correctiveActions.Add(correctiveAction);

        if (ClosureStatus == ClosureStatusOpen)
        {
            ClosureStatus = ClosureStatusInProgress;
        }

        return correctiveAction;
    }

    internal void Close(
        string closureNotes,
        Guid closedByUserId,
        DateTimeOffset closedAtUtc,
        Guid? finalEvidenceDocumentId)
    {
        if (ClosureStatus == ClosureStatusClosed)
        {
            throw new InvalidNonConformityTransitionException(Id, ClosureStatus, ClosureStatusClosed);
        }

        if (ClosureStatus == ClosureStatusOpen)
        {
            throw new InvalidNonConformityTransitionException(Id, ClosureStatus, ClosureStatusClosed);
        }

        ClosureStatus = ClosureStatusClosed;
        ClosureNotes = closureNotes;
        ClosedByUserId = closedByUserId;
        ClosedAtUtc = closedAtUtc;
        FinalEvidenceDocumentId = finalEvidenceDocumentId;
    }
}
