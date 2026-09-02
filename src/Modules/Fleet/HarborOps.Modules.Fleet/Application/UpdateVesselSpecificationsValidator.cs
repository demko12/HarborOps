using HarborOps.Modules.Fleet.Contracts;

namespace HarborOps.Modules.Fleet.Application;

internal static class UpdateVesselSpecificationsValidator
{
    internal const string DwtField = "dwt";
    internal const string GtField = "gt";
    internal const string LoaField = "loa";
    internal const string BeamField = "beam";
    internal const string DraftField = "draft";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        UpdateVesselSpecificationsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidatePositive(request.Dwt, DwtField, "Deadweight tonnage", errors);
        ValidatePositive(request.Gt, GtField, "Gross tonnage", errors);
        ValidatePositive(request.Loa, LoaField, "Length overall", errors);
        ValidatePositive(request.Beam, BeamField, "Beam", errors);
        ValidatePositive(request.Draft, DraftField, "Draft", errors);

        return errors;
    }

    private static void ValidatePositive(
        decimal? value,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (value is { } measurement && measurement <= 0)
        {
            errors[field] = [$"{displayName} must be greater than zero."];
        }
    }
}
