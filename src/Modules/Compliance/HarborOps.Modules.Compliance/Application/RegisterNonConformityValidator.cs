using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.SharedKernel;

namespace HarborOps.Modules.Compliance.Application;

internal static class RegisterNonConformityValidator
{
    internal const string SourceField = "source";
    internal const string SurveyIdField = "surveyId";
    internal const string PscIdField = "pscId";
    internal const string EvidenceDocumentIdField = "evidenceDocumentId";
    internal const string SeverityField = "severity";
    internal const string CorrectiveActionDeadlineField = "correctiveActionDeadline";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        RegisterNonConformityRequest request,
        IDateTimeProvider dateTimeProvider)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateSource(request.SurveyId, request.PscId, errors);

        ValidateRequiredGuid(
            request.EvidenceDocumentId,
            EvidenceDocumentIdField,
            "Evidence document",
            errors);

        ValidateSeverity(request.Severity, errors);

        ValidateCorrectiveActionDeadline(request.CorrectiveActionDeadline, dateTimeProvider, errors);

        return errors;
    }

    private static void ValidateSource(
        int? surveyId,
        int? pscId,
        IDictionary<string, string[]> errors)
    {
        if (surveyId is null && pscId is null)
        {
            errors[SourceField] = ["Exactly one of surveyId or pscId must be provided."];
            return;
        }

        if (surveyId is not null && pscId is not null)
        {
            errors[SourceField] = ["Exactly one of surveyId or pscId must be provided."];
        }
    }

    private static void ValidateSeverity(
        string? severity,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(severity))
        {
            errors[SeverityField] = ["Severity is required."];
            return;
        }

        var trimmedSeverity = severity.Trim();

        if (trimmedSeverity != NonConformity.SeverityMinor
            && trimmedSeverity != NonConformity.SeverityMajor
            && trimmedSeverity != NonConformity.SeverityCritical)
        {
            errors[SeverityField] =
            [
                $"Severity must be one of '{NonConformity.SeverityMinor}', '{NonConformity.SeverityMajor}', '{NonConformity.SeverityCritical}'.",
            ];
        }
    }

    private static void ValidateCorrectiveActionDeadline(
        DateOnly? correctiveActionDeadline,
        IDateTimeProvider dateTimeProvider,
        IDictionary<string, string[]> errors)
    {
        if (correctiveActionDeadline is null)
        {
            errors[CorrectiveActionDeadlineField] = ["Corrective action deadline is required."];
            return;
        }

        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow);

        if (correctiveActionDeadline.Value <= today)
        {
            errors[CorrectiveActionDeadlineField] = ["Corrective action deadline must be a future date."];
        }
    }

    private static void ValidateRequiredGuid(
        Guid? value,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (value is null || value == Guid.Empty)
        {
            errors[field] = [$"{displayName} is required."];
        }
    }
}
