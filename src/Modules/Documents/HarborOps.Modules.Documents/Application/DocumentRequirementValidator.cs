using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;

namespace HarborOps.Modules.Documents.Application;

internal static class DocumentRequirementValidator
{
    internal const string VesselIdField = "vesselId";
    internal const string DocumentTypeIdField = "documentTypeId";
    internal const string IsEnabledField = "isEnabled";
    internal const string MinimumRemainingValidityDaysField = "minimumRemainingValidityDays";
    internal const string InstructionField = "instruction";
    internal const string ReasonField = "reason";
    internal const string VersionField = "version";

    internal static IReadOnlyDictionary<string, string[]> ValidateCreate(CreateDocumentRequirementRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.VesselId is null || request.VesselId == Guid.Empty)
        {
            errors[VesselIdField] = ["Vessel is required."];
        }

        if (request.DocumentTypeId is null or < 1)
        {
            errors[DocumentTypeIdField] = ["Document type is required."];
        }

        ValidateSettings(request.MinimumRemainingValidityDays, request.Instruction, errors);
        ValidateReason(request.Reason, errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateUpdate(UpdateDocumentRequirementRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.IsEnabled is null)
        {
            errors[IsEnabledField] = ["Enabled state is required."];
        }

        ValidateSettings(request.MinimumRemainingValidityDays, request.Instruction, errors);
        ValidateReasonAndVersion(request.Reason, request.Version, errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateDisable(DisableDocumentRequirementRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateReasonAndVersion(request.Reason, request.Version, errors);

        return errors;
    }

    internal static string? NormalizeInstruction(string? instruction) =>
        string.IsNullOrWhiteSpace(instruction) ? null : instruction.Trim();

    private static void ValidateSettings(
        int? minimumRemainingValidityDays,
        string? instruction,
        Dictionary<string, string[]> errors)
    {
        if (minimumRemainingValidityDays is null)
        {
            errors[MinimumRemainingValidityDaysField] = ["Minimum remaining validity is required."];
        }
        else if (minimumRemainingValidityDays is < 0 or > VesselDocumentRequirement.MinimumRemainingValidityDaysLimit)
        {
            errors[MinimumRemainingValidityDaysField] =
            [
                $"Minimum remaining validity must be between 0 and {VesselDocumentRequirement.MinimumRemainingValidityDaysLimit} days.",
            ];
        }

        if (NormalizeInstruction(instruction) is { Length: > VesselDocumentRequirement.InstructionMaxLength })
        {
            errors[InstructionField] =
                [$"Instruction must not exceed {VesselDocumentRequirement.InstructionMaxLength} characters."];
        }
    }

    private static void ValidateReasonAndVersion(string? reason, Guid? version, Dictionary<string, string[]> errors)
    {
        ValidateReason(reason, errors);

        if (version is null || version == Guid.Empty)
        {
            errors[VersionField] = ["The requirement version is required."];
        }
    }

    private static void ValidateReason(string? reason, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            errors[ReasonField] = ["A reason is required."];
        }
        else if (reason.Trim().Length > VesselDocumentRequirementHistory.ReasonMaxLength)
        {
            errors[ReasonField] = [$"Reason must not exceed {VesselDocumentRequirementHistory.ReasonMaxLength} characters."];
        }
    }
}
