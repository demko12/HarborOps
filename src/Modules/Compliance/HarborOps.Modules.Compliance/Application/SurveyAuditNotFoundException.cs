namespace HarborOps.Modules.Compliance.Application;

internal sealed class SurveyAuditNotFoundException(int surveyId)
    : Exception($"Survey audit '{surveyId}' was not found.")
{
    internal int SurveyId { get; } = surveyId;
}
