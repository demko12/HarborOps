using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class RegisterEquipmentValidator
{
    internal const string VesselIdField = "vesselId";
    internal const string NameField = "name";
    internal const string CategoryField = "category";
    internal const string ManufacturerField = "manufacturer";
    internal const string SerialNumberField = "serialNumber";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterEquipmentRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.VesselId is null || request.VesselId == Guid.Empty)
        {
            errors[VesselIdField] = ["Select a vessel."];
        }

        ValidateRequiredText(request.Name, NameField, Equipment.NameMaxLength, errors);
        ValidateRequiredText(request.Category, CategoryField, Equipment.CategoryMaxLength, errors);
        ValidateOptionalText(request.Manufacturer, ManufacturerField, Equipment.ManufacturerMaxLength, errors);
        ValidateOptionalText(request.SerialNumber, SerialNumberField, Equipment.SerialNumberMaxLength, errors);

        return errors;
    }

    private static void ValidateRequiredText(
        string? value,
        string field,
        int maxLength,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = ["Required."];
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"Must not exceed {maxLength} characters."];
        }
    }

    private static void ValidateOptionalText(
        string? value,
        string field,
        int maxLength,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"Must not exceed {maxLength} characters."];
        }
    }
}
