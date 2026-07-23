using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;

namespace HarborOps.Modules.Fleet.Application;

internal static class RegisterVesselValidator
{
    internal const string FlagStateField = "flagState";
    internal const string ImoNumberField = "imoNumber";
    internal const string NameField = "name";
    internal const string VesselTypeField = "vesselType";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterVesselRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredLength(
            request.Name,
            Vessel.NameMaxLength,
            NameField,
            "Vessel name",
            errors);

        ValidateImoNumber(request.ImoNumber, errors);

        ValidateRequiredLength(
            request.FlagState,
            Vessel.FlagStateMaxLength,
            FlagStateField,
            "Flag state",
            errors);

        ValidateRequiredLength(
            request.VesselType,
            Vessel.VesselTypeMaxLength,
            VesselTypeField,
            "Vessel type",
            errors);

        return errors;
    }

    private static void ValidateImoNumber(
        string? imoNumber,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(imoNumber))
        {
            errors[ImoNumberField] = ["IMO number is required."];
            return;
        }

        var normalizedImoNumber = imoNumber.Trim();

        if (normalizedImoNumber.Length != Vessel.ImoNumberLength
            || !normalizedImoNumber.All(char.IsAsciiDigit))
        {
            errors[ImoNumberField] = ["IMO number must contain exactly seven digits."];
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
