using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.Modules.Compliance.Application;

internal static class RegisterPscInspectionValidator
{
    internal const string VesselIdField = "vesselId";
    internal const string PortIdField = "portId";
    internal const string InspectionDateField = "inspectionDate";
    internal const string InspectingAuthorityField = "inspectingAuthority";
    internal const string DeficiencyCountField = "deficiencyCount";
    internal const string VesselDetainedField = "vesselDetained";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterPscInspectionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredGuid(
            request.VesselId,
            VesselIdField,
            "Vessel",
            errors);

        ValidateRequiredGuid(
            request.PortId,
            PortIdField,
            "Port",
            errors);

        ValidateInspectionDate(request.InspectionDate, errors);

        ValidateRequiredLength(
            request.InspectingAuthority,
            PscInspection.InspectingAuthorityMaxLength,
            InspectingAuthorityField,
            "Inspecting authority",
            errors);

        ValidateDeficiencyCount(request.DeficiencyCount, errors);

        ValidateVesselDetained(request.VesselDetained, errors);

        return errors;
    }

    private static void ValidateInspectionDate(
        DateOnly? inspectionDate,
        IDictionary<string, string[]> errors)
    {
        if (inspectionDate is null)
        {
            errors[InspectionDateField] = ["Inspection date is required."];
            return;
        }

        if (inspectionDate.Value > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            errors[InspectionDateField] = ["Inspection date must not be in the future."];
        }
    }

    private static void ValidateDeficiencyCount(
        int? deficiencyCount,
        IDictionary<string, string[]> errors)
    {
        if (deficiencyCount is null)
        {
            errors[DeficiencyCountField] = ["Deficiency count is required."];
            return;
        }

        if (deficiencyCount.Value < 0)
        {
            errors[DeficiencyCountField] = ["Deficiency count must not be negative."];
        }
    }

    private static void ValidateVesselDetained(
        bool? vesselDetained,
        IDictionary<string, string[]> errors)
    {
        if (vesselDetained is null)
        {
            errors[VesselDetainedField] = ["Vessel detained is required."];
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
