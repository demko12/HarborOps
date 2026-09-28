using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class ContainerEntryValidator
{
    internal const decimal MaximumWeightKg = 999999999999999.999m;
    internal static readonly IReadOnlySet<string> SupportedTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "22G1", "42G1", "45G1", "22R1", "42R1", "22U1", "42U1", "22P1", "42P1", "22T1",
        "20GP", "40HC", "40RH",
    };

    internal static IReadOnlyDictionary<string, string[]> Validate(AddCargoContainerRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(request.ContainerNumber)) errors["containerNumber"] = ["Container number is required."];
        else if (Container.NormalizeNumber(request.ContainerNumber).Length > Container.ContainerNumberMaxLength) errors["containerNumber"] = ["Container number must not exceed 20 characters."];
        if (string.IsNullOrWhiteSpace(request.ContainerType)) errors["containerType"] = ["Container type is required."];
        else if (!SupportedTypes.Contains(request.ContainerType.Trim().ToUpperInvariant())) errors["containerType"] = ["Container type must be a supported ISO 6346 code."];
        ValidatePort(request.PortOfLoadingCode, "portOfLoadingCode", "POL", errors);
        ValidatePort(request.PortOfDischargeCode, "portOfDischargeCode", "POD", errors);
        if (!string.IsNullOrWhiteSpace(request.PortOfLoadingCode) && !string.IsNullOrWhiteSpace(request.PortOfDischargeCode) && string.Equals(request.PortOfLoadingCode.Trim(), request.PortOfDischargeCode.Trim(), StringComparison.OrdinalIgnoreCase)) errors["portOfDischargeCode"] = ["POD must differ from POL."];
        if (request.WeightKg is null || request.WeightKg <= 0) errors["weightKg"] = ["Weight in kg must be greater than zero."];
        else if (request.WeightKg > MaximumWeightKg) errors["weightKg"] = ["Weight in kg exceeds the supported range."];
        else if (decimal.Round(request.WeightKg.Value, Container.WeightKgScale) != request.WeightKg.Value) errors["weightKg"] = ["Weight in kg supports at most three decimal places."];
        return errors;
    }

    internal static string NormalizePort(string value) => value.Trim().ToUpperInvariant();

    internal static bool IsPortCode(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length == Container.PortCodeLength && value.Trim().All(char.IsLetterOrDigit);

    private static void ValidatePort(string? value, string field, string label, IDictionary<string, string[]> errors)
    {
        if (!IsPortCode(value)) errors[field] = [$"{label} must be a five-character UN/LOCODE."];
    }
}
