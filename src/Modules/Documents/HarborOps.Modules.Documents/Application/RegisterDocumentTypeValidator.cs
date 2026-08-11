using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;

namespace HarborOps.Modules.Documents.Application;

internal static class RegisterDocumentTypeValidator
{
    internal const string NameField = "name";
    internal const string MandatoryValidityPeriodDaysField = "mandatoryValidityPeriodDays";
    internal const string IssuingAuthorityField = "issuingAuthority";
    internal const string RequiresSurveyField = "requiresSurvey";

    internal const int MinMandatoryValidityPeriodDays = 1;
    internal const int MaxMandatoryValidityPeriodDays = 3650;

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterDocumentTypeRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredLength(
            request.Name,
            DocumentType.NameMaxLength,
            NameField,
            "Name",
            errors);

        ValidateMandatoryValidityPeriodDays(request.MandatoryValidityPeriodDays, errors);

        ValidateRequiredLength(
            request.IssuingAuthority,
            DocumentType.IssuingAuthorityMaxLength,
            IssuingAuthorityField,
            "Issuing authority",
            errors);

        ValidateRequiresSurvey(request.RequiresSurvey, errors);

        return errors;
    }

    private static void ValidateMandatoryValidityPeriodDays(
        int? value,
        IDictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[MandatoryValidityPeriodDaysField] = ["Mandatory validity period is required."];
            return;
        }

        if (value < MinMandatoryValidityPeriodDays || value > MaxMandatoryValidityPeriodDays)
        {
            errors[MandatoryValidityPeriodDaysField] =
                [$"Mandatory validity period must be between {MinMandatoryValidityPeriodDays} and {MaxMandatoryValidityPeriodDays} days."];
        }
    }

    private static void ValidateRequiresSurvey(
        bool? value,
        IDictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[RequiresSurveyField] = ["Requires survey is required."];
        }
    }

    private static void ValidateRequiredLength(
        string? value,
        int maxLength,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{displayName} is required."];
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"{displayName} must not exceed {maxLength} characters."];
        }
    }
}
