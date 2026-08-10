using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class DeclareCargoSafetyInfoValidator
{
    internal const string HandlingNotesField = "handlingNotes";
    internal const string ImdgClassField = "imdgClass";
    internal const string StorageConditionsField = "storageConditions";
    internal const string UnNumberField = "unNumber";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        DeclareCargoSafetyInfoRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredLength(
            request.ImdgClass,
            CargoSafetyInfo.ImdgClassMaxLength,
            ImdgClassField,
            "IMDG class",
            errors);

        ValidateRequiredLength(
            request.UnNumber,
            CargoSafetyInfo.UnNumberMaxLength,
            UnNumberField,
            "UN number",
            errors);

        ValidateRequiredLength(
            request.StorageConditions,
            CargoSafetyInfo.StorageConditionsMaxLength,
            StorageConditionsField,
            "Storage conditions",
            errors);

        ValidateOptionalLength(
            request.HandlingNotes,
            CargoSafetyInfo.HandlingNotesMaxLength,
            HandlingNotesField,
            "Handling notes",
            errors);

        return errors;
    }

    private static void ValidateOptionalLength(
        string? value,
        int maxLength,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"{displayName} must not exceed {maxLength} characters."];
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
