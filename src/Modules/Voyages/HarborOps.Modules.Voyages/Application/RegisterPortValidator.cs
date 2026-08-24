using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.Modules.Voyages.Application;

internal static class RegisterPortValidator
{
    internal const string CityField = "city";
    internal const string CountryField = "country";
    internal const string LatitudeField = "latitude";
    internal const string LongitudeField = "longitude";
    internal const string NameField = "name";
    internal const string ShipyardNameField = "shipyardName";
    internal const string UnLocodeField = "unLocode";

    internal const decimal LatitudeLimit = 90m;
    internal const decimal LongitudeLimit = 180m;

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterPortRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredText(request.Name, Port.NameMaxLength, NameField, "Port name", errors);
        ValidateRequiredText(request.Country, Port.CountryMaxLength, CountryField, "Country", errors);
        ValidateOptionalText(request.City, Port.CityMaxLength, CityField, "City", errors);
        ValidateOptionalText(
            request.ShipyardName,
            Port.ShipyardNameMaxLength,
            ShipyardNameField,
            "Shipyard name",
            errors);
        ValidateUnLocode(request.UnLocode, errors);
        ValidateCoordinates(request, errors);

        return errors;
    }

    internal static string NormalizeUnLocode(string unLocode)
    {
        return unLocode.Trim().ToUpperInvariant();
    }

    private static void ValidateUnLocode(
        string? value,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var unLocode = value.Trim();

        if (unLocode.Length != Port.UnLocodeLength)
        {
            errors[UnLocodeField] =
                [$"UN/LOCODE must contain exactly {Port.UnLocodeLength} characters."];
            return;
        }

        if (!unLocode.All(char.IsAsciiLetterOrDigit))
        {
            errors[UnLocodeField] = ["UN/LOCODE must contain only letters and digits."];
        }
    }

    private static void ValidateCoordinates(
        RegisterPortRequest request,
        IDictionary<string, string[]> errors)
    {
        if (request.Latitude is null != request.Longitude is null)
        {
            var field = request.Latitude is null ? LatitudeField : LongitudeField;
            errors[field] = ["Latitude and longitude must be supplied together."];
            return;
        }

        if (request.Latitude is { } latitude
            && (latitude < -LatitudeLimit || latitude > LatitudeLimit))
        {
            errors[LatitudeField] =
                [$"Latitude must be between -{LatitudeLimit} and {LatitudeLimit}."];
        }

        if (request.Longitude is { } longitude
            && (longitude < -LongitudeLimit || longitude > LongitudeLimit))
        {
            errors[LongitudeField] =
                [$"Longitude must be between -{LongitudeLimit} and {LongitudeLimit}."];
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

    private static void ValidateOptionalText(
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
