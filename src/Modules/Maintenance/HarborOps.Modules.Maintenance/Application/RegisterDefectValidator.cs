using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class RegisterDefectValidator
{
    internal const string VesselIdField = "vesselId";
    internal const string EquipmentIdField = "equipmentId";
    internal const string TitleField = "title";
    internal const string DescriptionField = "description";
    internal const string ReportedSeverityField = "reportedSeverity";
    internal const string ObservedAtUtcField = "observedAtUtc";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterDefectRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.VesselId is null || request.VesselId == Guid.Empty)
        {
            errors[VesselIdField] = ["Vessel ID is required."];
        }

        ValidateTitle(request.Title, errors);
        ValidateDescription(request.Description, errors);
        ValidateReportedSeverity(request.ReportedSeverity, errors);
        ValidateObservedAtUtc(request.ObservedAtUtc, errors);

        return errors;
    }

    private static void ValidateTitle(string? title, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            errors[TitleField] = ["Title is required."];
            return;
        }

        if (title.Length > VesselDefect.TitleMaxLength)
        {
            errors[TitleField] = [$"Title must not exceed {VesselDefect.TitleMaxLength} characters."];
        }
    }

    private static void ValidateDescription(string? description, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        if (description.Length > VesselDefect.DescriptionMaxLength)
        {
            errors[DescriptionField] = [$"Description must not exceed {VesselDefect.DescriptionMaxLength} characters."];
        }
    }

    private static void ValidateReportedSeverity(string? reportedSeverity, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(reportedSeverity)
            || !Enum.TryParse(reportedSeverity, ignoreCase: true, out DefectSeverity _))
        {
            errors[ReportedSeverityField] = ["Reported severity must be one of: Low, Medium, High, Critical."];
        }
    }

    private static void ValidateObservedAtUtc(DateTime? observedAtUtc, IDictionary<string, string[]> errors)
    {
        if (observedAtUtc is null)
        {
            errors[ObservedAtUtcField] = ["Observed date is required."];
        }
    }
}
