using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class CargoDiscrepancyValidator
{
    internal const string TypeField = "type";
    internal const string SeverityField = "severity";
    internal const string ContainerIdField = "containerId";
    internal const string ObservedAtUtcField = "observedAtUtc";
    internal const string DescriptionField = "description";
    internal const string NoteField = "note";
    internal const string DispositionField = "disposition";
    internal const string SummaryField = "summary";
    internal const string ReasonField = "reason";
    internal const string StatusField = "status";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> ValidateRegistration(
        RegisterCargoDiscrepancyRequest request,
        DateTime utcNow)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            errors[TypeField] = ["A discrepancy type is required."];
        }
        else if (!TryParse<CargoDiscrepancyType>(request.Type, out var type))
        {
            errors[TypeField] = [$"Type must be one of: {Names<CargoDiscrepancyType>()}."];
        }
        else if (request.ContainerId is null && CargoDiscrepancy.RequiresContainer(type))
        {
            errors[ContainerIdField] = [$"A {type} discrepancy must name the container it was observed on."];
        }

        ValidateSeverity(request.Severity, errors);
        ValidateObservedAt(request.ObservedAtUtc, required: true, utcNow, errors);
        ValidateRequiredText(
            request.Description,
            DescriptionField,
            CargoDiscrepancy.DescriptionMaxLength,
            "A description",
            errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateExcursionRegistration(
        OpenExcursionDiscrepancyRequest request,
        DateTime utcNow)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateSeverity(request.Severity, errors);
        ValidateObservedAt(request.ObservedAtUtc, required: false, utcNow, errors);
        ValidateRequiredText(
            request.Description,
            DescriptionField,
            CargoDiscrepancy.DescriptionMaxLength,
            "A description",
            errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidatePage(GetCargoDiscrepanciesRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(request.Status) && !TryParse<CargoDiscrepancyStatus>(request.Status, out _))
        {
            errors[StatusField] = [$"Status must be one of: {Names<CargoDiscrepancyStatus>()}."];
        }

        if (!string.IsNullOrWhiteSpace(request.Type) && !TryParse<CargoDiscrepancyType>(request.Type, out _))
        {
            errors[TypeField] = [$"Type must be one of: {Names<CargoDiscrepancyType>()}."];
        }

        if (!string.IsNullOrWhiteSpace(request.Severity) && !TryParse<CargoDiscrepancySeverity>(request.Severity, out _))
        {
            errors[SeverityField] = [$"Severity must be one of: {Names<CargoDiscrepancySeverity>()}."];
        }

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

    internal static IReadOnlyDictionary<string, string[]> ValidateNote(AddCargoDiscrepancyNoteRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredText(request.Note, NoteField, CargoDiscrepancyEvent.TextMaxLength, "A note", errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateResolution(ResolveCargoDiscrepancyRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Disposition))
        {
            errors[DispositionField] = ["A disposition is required."];
        }
        else if (!TryParse<CargoDiscrepancyDisposition>(request.Disposition, out _))
        {
            errors[DispositionField] = [$"Disposition must be one of: {Names<CargoDiscrepancyDisposition>()}."];
        }

        var summary = request.Summary?.Trim();

        if (string.IsNullOrEmpty(summary))
        {
            errors[SummaryField] = ["A resolution summary is required."];
        }
        else if (summary.Length < CargoDiscrepancy.ResolutionSummaryMinLength)
        {
            errors[SummaryField] =
                [$"The resolution summary must be at least {CargoDiscrepancy.ResolutionSummaryMinLength} characters."];
        }
        else if (summary.Length > CargoDiscrepancyEvent.TextMaxLength)
        {
            errors[SummaryField] =
                [$"The resolution summary must not exceed {CargoDiscrepancyEvent.TextMaxLength} characters."];
        }

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateVoid(VoidCargoDiscrepancyRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredText(request.Reason, ReasonField, CargoDiscrepancyEvent.TextMaxLength, "A void reason", errors);

        return errors;
    }

    internal static bool TryParse<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;

        return !string.IsNullOrWhiteSpace(value)
            && !int.TryParse(value, out _)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out parsed)
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

    internal static IReadOnlyDictionary<string, string[]> SingleError(string field, string message)
    {
        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [message],
        };
    }

    private static void ValidateSeverity(string? severity, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(severity))
        {
            errors[SeverityField] = ["A severity is required."];
        }
        else if (!TryParse<CargoDiscrepancySeverity>(severity, out _))
        {
            errors[SeverityField] = [$"Severity must be one of: {Names<CargoDiscrepancySeverity>()}."];
        }
    }

    private static void ValidateObservedAt(
        DateTime? observedAtUtc,
        bool required,
        DateTime utcNow,
        IDictionary<string, string[]> errors)
    {
        if (observedAtUtc is not { } observedAt)
        {
            if (required)
            {
                errors[ObservedAtUtcField] = ["An observation time is required."];
            }

            return;
        }

        if (NormalizeToUtc(observedAt) > utcNow)
        {
            errors[ObservedAtUtcField] = ["An observation time must not be in the future."];
        }
    }

    private static void ValidateRequiredText(
        string? value,
        string field,
        int maxLength,
        string label,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{label} is required."];
        }
        else if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"{label} must not exceed {maxLength} characters."];
        }
    }

    private static string Names<TEnum>()
        where TEnum : struct, Enum
    {
        return string.Join(", ", Enum.GetNames<TEnum>());
    }
}
