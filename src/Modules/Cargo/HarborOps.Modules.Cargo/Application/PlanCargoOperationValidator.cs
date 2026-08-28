using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class PlanCargoOperationValidator
{
    internal const string OperationTypeField = "operationType";
    internal const string PlannedAtUtcField = "plannedAtUtc";

    internal static IReadOnlyDictionary<string, string[]> Validate(PlanCargoOperationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateOperationType(request.OperationType, errors);
        ValidatePlannedAtUtc(request.PlannedAtUtc, errors);

        return errors;
    }

    internal static bool TryParseOperationType(
        string? operationType,
        out CargoOperationType parsedOperationType)
    {
        return Enum.TryParse(operationType, ignoreCase: true, out parsedOperationType)
            && Enum.IsDefined(parsedOperationType);
    }

    private static void ValidateOperationType(
        string? operationType,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(operationType))
        {
            errors[OperationTypeField] = ["Operation type is required."];
            return;
        }

        if (!TryParseOperationType(operationType, out _))
        {
            errors[OperationTypeField] = ["Operation type must be Load or Unload."];
        }
    }

    private static void ValidatePlannedAtUtc(
        DateTime? plannedAtUtc,
        IDictionary<string, string[]> errors)
    {
        if (plannedAtUtc is null)
        {
            errors[PlannedAtUtcField] = ["Planned at UTC is required."];
            return;
        }

        if (plannedAtUtc.Value.Kind != DateTimeKind.Utc)
        {
            errors[PlannedAtUtcField] = ["Planned at UTC must be in UTC format."];
        }
    }
}
