using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.Modules.Voyages.Application;

internal static class RegisterBerthValidator
{
    internal const string LengthField = "length";
    internal const string MaxDraftField = "maxDraft";
    internal const string NameField = "name";
    internal const string PortIdField = "portId";
    internal const string StatusField = "status";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterBerthRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.PortId is null || request.PortId == Guid.Empty)
        {
            errors[PortIdField] = ["Port is required."];
        }

        ValidateRequiredText(request.Name, Berth.NameMaxLength, NameField, "Berth name", errors);
        ValidateStatus(request.Status, errors);
        ValidatePositive(request.Length, LengthField, "Length", errors);
        ValidatePositive(request.MaxDraft, MaxDraftField, "Maximum draft", errors);

        return errors;
    }

    internal static string NormalizeName(string name)
    {
        return name.Trim().ToUpperInvariant();
    }

    private static void ValidateStatus(
        string? value,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[StatusField] = ["Status is required."];
            return;
        }

        if (!BerthStatus.IsValid(value.Trim()))
        {
            errors[StatusField] =
                [$"Status must be one of: {string.Join(", ", BerthStatus.All)}."];
        }
    }

    private static void ValidatePositive(
        decimal? value,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[field] = [$"{displayName} is required."];
            return;
        }

        if (value.Value <= 0)
        {
            errors[field] = [$"{displayName} must be greater than zero."];
        }
    }

    private static void ValidateRequiredText(
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
