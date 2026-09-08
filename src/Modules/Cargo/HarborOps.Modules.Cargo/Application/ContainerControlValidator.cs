using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class ContainerControlValidator
{
    internal const string ConditionNotesField = "conditionNotes";
    internal const string NotesField = "notes";
    internal const string ReasonField = "reason";
    internal const string ResultField = "result";
    internal const string SealNumberField = "sealNumber";
    internal const string TemperatureCelsiusField = "temperatureCelsius";

    internal static IReadOnlyDictionary<string, string[]> ValidateSeal(
        AssignContainerSealRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateSealNumber(request.SealNumber, errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateInspection(
        RecordContainerInspectionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateResult(request.Result, errors);
        ValidateConditionNotes(request.ConditionNotes, errors);
        ValidateTemperature(request.TemperatureCelsius, errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateRelease(
        ReleaseContainerRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateOptionalText(
            request.Notes,
            NotesField,
            ContainerControl.ReasonMaxLength,
            "Release notes",
            errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateSealBreak(
        BreakContainerSealRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateOptionalText(
            request.Notes,
            NotesField,
            ContainerControl.ReasonMaxLength,
            "Seal break notes",
            errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateHold(
        HoldContainerRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["A hold reason is required."];

            return errors;
        }

        ValidateOptionalText(
            request.Reason,
            ReasonField,
            ContainerControl.ReasonMaxLength,
            "A hold reason",
            errors);

        return errors;
    }

    internal static bool TryParseResult(string? result, out ContainerInspectionResult parsed)
    {
        return Enum.TryParse(result, ignoreCase: true, out parsed)
            && parsed is ContainerInspectionResult.Passed or ContainerInspectionResult.Failed;
    }

    internal static IReadOnlyDictionary<string, string[]> TemperatureNotAllowedError()
    {
        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [TemperatureCelsiusField] =
                ["A temperature can only be recorded for a reefer container."],
        };
    }

    private static void ValidateSealNumber(
        string? sealNumber,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(sealNumber))
        {
            errors[SealNumberField] = ["A seal number is required."];

            return;
        }

        var normalized = Container.NormalizeSealNumber(sealNumber);

        if (normalized.Length > ContainerControl.SealNumberMaxLength)
        {
            errors[SealNumberField] =
                [$"Seal number must not exceed {ContainerControl.SealNumberMaxLength} characters."];

            return;
        }

        if (!normalized.All(character => char.IsAsciiLetterOrDigit(character) || character is '-'))
        {
            errors[SealNumberField] =
                ["Seal number may only contain letters, digits and hyphens."];
        }
    }

    private static void ValidateResult(string? result, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(result))
        {
            errors[ResultField] = ["An inspection result is required."];

            return;
        }

        if (!TryParseResult(result, out _))
        {
            errors[ResultField] =
                [$"Result must be '{nameof(ContainerInspectionResult.Passed)}' or '{nameof(ContainerInspectionResult.Failed)}'."];
        }
    }

    private static void ValidateConditionNotes(
        string? conditionNotes,
        IDictionary<string, string[]> errors)
    {
        ValidateOptionalText(
            conditionNotes,
            ConditionNotesField,
            ContainerControl.InspectionNotesMaxLength,
            "Condition notes",
            errors);
    }

    private static void ValidateTemperature(
        decimal? temperatureCelsius,
        IDictionary<string, string[]> errors)
    {
        if (temperatureCelsius is not { } temperature)
        {
            return;
        }

        if (temperature < ContainerControl.MinimumTemperatureCelsius
            || temperature > ContainerControl.MaximumTemperatureCelsius)
        {
            errors[TemperatureCelsiusField] =
                [$"Temperature must be between {ContainerControl.MinimumTemperatureCelsius} and {ContainerControl.MaximumTemperatureCelsius} degrees Celsius."];
        }
    }

    private static void ValidateOptionalText(
        string? value,
        string field,
        int maxLength,
        string label,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"{label} must not exceed {maxLength} characters."];
        }
    }
}
