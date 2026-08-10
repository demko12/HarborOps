namespace HarborOps.Api.Composition.Operations;

public static class FleetVesselOverviewValidator
{
    public const int MaxPageSize = 100;

    public const string FleetIdField = "fleetId";
    public const string PageNumberField = "pageNumber";
    public const string PageSizeField = "pageSize";

    public static IReadOnlyDictionary<string, string[]> Validate(
        Guid fleetId,
        int pageNumber,
        int pageSize)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (fleetId == Guid.Empty)
        {
            errors[FleetIdField] = ["Fleet is required."];
        }

        if (pageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be greater than zero."];
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            errors[PageSizeField] =
                [$"Page size must be between 1 and {MaxPageSize}."];
        }

        return errors;
    }
}
