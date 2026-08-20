namespace HarborOps.Modules.Compliance.Domain;

public sealed class SurveyAudit
{
    public const string TypeAnnualSurvey = "Annual survey";
    public const string TypeIntermediateSurvey = "Intermediate survey";
    public const string TypeInternalAudit = "Internal audit";
    public const string TypeExternalAudit = "External audit";
    public const string TypeFlagStateInspection = "Flag state inspection";
    public const string ResultSatisfactory = "Satisfactory";
    public const string ResultFindingsRaised = "Findings raised";
    public const string ResultUnsatisfactory = "Unsatisfactory";

    public static IReadOnlyList<string> SupportedTypes { get; } =
        [TypeAnnualSurvey, TypeIntermediateSurvey, TypeInternalAudit, TypeExternalAudit, TypeFlagStateInspection];

    public static IReadOnlyList<string> SupportedResults { get; } =
        [ResultSatisfactory, ResultFindingsRaised, ResultUnsatisfactory];
    internal const int TypeMaxLength = 100;
    internal const int InspectorNameMaxLength = 200;
    internal const int ResultMaxLength = 100;
    internal const int FindingsMaxLength = 4000;

    public int Id { get; private set; }

    public Guid VesselId { get; private set; }

    public Guid? DocumentId { get; private set; }

    public DateOnly Date { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string? InspectorName { get; private set; }

    public string Result { get; private set; } = string.Empty;

    public string Findings { get; private set; } = string.Empty;

    private SurveyAudit()
    {
    }

    public SurveyAudit(
        Guid vesselId,
        DateOnly date,
        string type,
        string result,
        string findings,
        Guid? documentId = null,
        string? inspectorName = null)
    {
        VesselId = vesselId;
        Date = date;
        Type = type;
        Result = result;
        Findings = findings;
        DocumentId = documentId;
        InspectorName = inspectorName;
    }
}
