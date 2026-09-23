using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class ReeferMonitoringValidator
{
    internal const string DispositionField = "disposition";
    internal const string ExplanationField = "explanation";
    internal const string MaximumCelsiusField = "maximumCelsius";
    internal const string MeasuredAtUtcField = "measuredAtUtc";
    internal const string MinimumCelsiusField = "minimumCelsius";
    internal const string NotesField = "notes";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";
    internal const string ReasonField = "reason";
    internal const string TargetCelsiusField = "targetCelsius";
    internal const string TemperatureCelsiusField = "temperatureCelsius";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> ValidateProfile(
        SaveReeferMonitoringProfileRequest request,
        bool requiresReason)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var minimum = ValidateThreshold(
            request.MinimumCelsius,
            MinimumCelsiusField,
            "A minimum temperature",
            errors);
        var maximum = ValidateThreshold(
            request.MaximumCelsius,
            MaximumCelsiusField,
            "A maximum temperature",
            errors);
        var target = ValidateThreshold(
            request.TargetCelsius,
            TargetCelsiusField,
            "A target temperature",
            errors);

        if (minimum is { } minimumCelsius && maximum is { } maximumCelsius)
        {
            if (minimumCelsius > maximumCelsius)
            {
                errors[MinimumCelsiusField] =
                    ["The minimum temperature must not be above the maximum temperature."];
            }
            else if (target is { } targetCelsius
                && (targetCelsius < minimumCelsius || targetCelsius > maximumCelsius))
            {
                errors[TargetCelsiusField] =
                    ["The target temperature must be within the acceptable range."];
            }
        }

        if (requiresReason && string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["A reason is required when the thresholds change."];
        }
        else
        {
            ValidateOptionalText(
                request.Reason,
                ReasonField,
                ReeferMonitoringProfile.ReasonMaxLength,
                "A reason",
                errors);
        }

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateReadingPage(
        GetReeferTemperatureReadingsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.PageNumber is { } pageNumber && pageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize is { } pageSize)
        {
            if (pageSize < 1)
            {
                errors[PageSizeField] = ["Page size must be 1 or greater."];
            }
            else if (pageSize > MaxPageSize)
            {
                errors[PageSizeField] = [$"Page size must not exceed {MaxPageSize}."];
            }
        }

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateReading(
        RecordReeferTemperatureReadingRequest request,
        DateTime utcNow)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateTemperature(request.TemperatureCelsius, errors);
        ValidateMeasuredAt(request.MeasuredAtUtc, utcNow, errors);
        ValidateOptionalText(
            request.Notes,
            NotesField,
            ReeferTemperatureReading.NotesMaxLength,
            "Notes",
            errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateCorrection(
        CorrectReeferTemperatureReadingRequest request,
        DateTime utcNow)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateTemperature(request.TemperatureCelsius, errors);
        ValidateMeasuredAt(request.MeasuredAtUtc, utcNow, errors);
        ValidateOptionalText(
            request.Notes,
            NotesField,
            ReeferTemperatureReading.NotesMaxLength,
            "Notes",
            errors);
        ValidateRequiredReason(request.Reason, "A correction reason", errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateVoid(
        VoidReeferTemperatureReadingRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredReason(request.Reason, "A void reason", errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateResolution(
        ResolveReeferExcursionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Disposition))
        {
            errors[DispositionField] = ["A disposition is required."];
        }
        else if (!TryParseDisposition(request.Disposition, out _))
        {
            errors[DispositionField] =
                [$"Disposition must be '{nameof(ReeferExcursionDisposition.CorrectiveAction)}' or '{nameof(ReeferExcursionDisposition.AcceptedDeviation)}'."];
        }

        if (string.IsNullOrWhiteSpace(request.Explanation))
        {
            errors[ExplanationField] = ["An explanation is required."];
        }
        else
        {
            ValidateOptionalText(
                request.Explanation,
                ExplanationField,
                ReeferExcursion.ExplanationMaxLength,
                "An explanation",
                errors);
        }

        return errors;
    }

    internal static bool TryParseDisposition(
        string? disposition,
        out ReeferExcursionDisposition parsed)
    {
        return Enum.TryParse(disposition, ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed);
    }

    internal static DateTime NormalizeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    internal static IReadOnlyDictionary<string, string[]> ContainerIsNotReeferError()
    {
        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["containerId"] =
                ["Temperature monitoring is only available for reefer containers."],
        };
    }

    internal static IReadOnlyDictionary<string, string[]> ProfileMissingError()
    {
        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["profile"] =
                ["A monitoring profile must be configured before readings can be recorded."],
        };
    }

    private static decimal? ValidateThreshold(
        decimal? value,
        string field,
        string label,
        IDictionary<string, string[]> errors)
    {
        if (value is not { } threshold)
        {
            errors[field] = [$"{label} is required."];

            return null;
        }

        if (!ReeferTemperature.IsWithinSupportedRange(threshold))
        {
            errors[field] =
                [$"{label} must be between {ContainerControl.MinimumTemperatureCelsius} and {ContainerControl.MaximumTemperatureCelsius} degrees Celsius."];

            return null;
        }

        if (!ReeferTemperature.HasSupportedPrecision(threshold))
        {
            errors[field] =
                [$"{label} must not have more than {ContainerControl.TemperatureScale} decimal places."];

            return null;
        }

        return threshold;
    }

    private static void ValidateTemperature(
        decimal? temperatureCelsius,
        IDictionary<string, string[]> errors)
    {
        ValidateThreshold(
            temperatureCelsius,
            TemperatureCelsiusField,
            "A temperature",
            errors);
    }

    private static void ValidateMeasuredAt(
        DateTime? measuredAtUtc,
        DateTime utcNow,
        IDictionary<string, string[]> errors)
    {
        if (measuredAtUtc is not { } measuredAt)
        {
            errors[MeasuredAtUtcField] = ["A measurement time is required."];

            return;
        }

        if (NormalizeToUtc(measuredAt) > utcNow)
        {
            errors[MeasuredAtUtcField] = ["A measurement time must not be in the future."];
        }
    }

    private static void ValidateRequiredReason(
        string? reason,
        string label,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            errors[ReasonField] = [$"{label} is required."];

            return;
        }

        ValidateOptionalText(
            reason,
            ReasonField,
            ReeferTemperatureReading.ReasonMaxLength,
            label,
            errors);
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
