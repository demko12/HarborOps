using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;

namespace HarborOps.Modules.Fleet.Application;

internal static class UpdateVesselRegistryDetailsValidator
{
    internal const string VesselTypeIdField = "vesselTypeId";
    internal const string NameField = "name";
    internal const string MmsiField = "mmsi";
    internal const string CallSignField = "callSign";
    internal const string FlagStateField = "flagState";
    internal const string PortOfRegistryField = "portOfRegistry";
    internal const string ClassificationSocietyField = "classificationSociety";
    internal const string LifecycleStageField = "lifecycleStage";
    internal const string MsmdIdField = "msmdId";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        UpdateVesselRegistryDetailsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.VesselTypeId is null || request.VesselTypeId == Guid.Empty)
        {
            errors[VesselTypeIdField] = ["Vessel type is required."];
        }

        ValidateRequiredLength(
            request.Name,
            Vessel.NameMaxLength,
            NameField,
            "Vessel name",
            errors);

        ValidateRequiredLength(
            request.FlagState,
            Vessel.FlagStateMaxLength,
            FlagStateField,
            "Flag state",
            errors);

        ValidateMmsi(request.Mmsi, errors);
        ValidateCallSign(request.CallSign, errors);

        ValidateOptionalLength(
            request.PortOfRegistry,
            Vessel.PortOfRegistryMaxLength,
            PortOfRegistryField,
            "Port of registry",
            errors);

        ValidateOptionalLength(
            request.ClassificationSociety,
            Vessel.ClassificationSocietyMaxLength,
            ClassificationSocietyField,
            "Classification society",
            errors);

        ValidateOptionalLength(
            request.LifecycleStage,
            Vessel.LifecycleStageMaxLength,
            LifecycleStageField,
            "Lifecycle stage",
            errors);

        ValidateOptionalLength(
            request.MsmdId,
            Vessel.MsmdIdMaxLength,
            MsmdIdField,
            "MSMD identifier",
            errors);

        return errors;
    }

    private static void ValidateMmsi(string? mmsi, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(mmsi))
        {
            return;
        }

        var normalized = mmsi.Trim();

        if (normalized.Length > Vessel.MmsiMaxLength)
        {
            errors[MmsiField] = [$"MMSI must not exceed {Vessel.MmsiMaxLength} characters."];
            return;
        }

        if (!normalized.All(char.IsAsciiDigit))
        {
            errors[MmsiField] = ["MMSI must contain digits only."];
        }
    }

    private static void ValidateCallSign(string? callSign, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(callSign))
        {
            return;
        }

        var normalized = callSign.Trim();

        if (normalized.Length > Vessel.CallSignMaxLength)
        {
            errors[CallSignField] =
                [$"Call sign must not exceed {Vessel.CallSignMaxLength} characters."];
            return;
        }

        if (!normalized.All(char.IsAsciiLetterOrDigit))
        {
            errors[CallSignField] = ["Call sign must contain letters and digits only."];
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

    private static void ValidateOptionalLength(
        string? value,
        int maxLength,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
        {
            errors[field] = [$"{displayName} must not exceed {maxLength} characters."];
        }
    }
}
