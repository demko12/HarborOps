using System.Globalization;

namespace HarborOps.Web.Components.Pages.Voyages;

internal static class MasterDataFormat
{
    internal const string MissingValue = "—";

    internal static string OptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? MissingValue : value;
    }

    internal static string Coordinates(decimal? latitude, decimal? longitude)
    {
        if (latitude is not { } latitudeValue || longitude is not { } longitudeValue)
        {
            return MissingValue;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{latitudeValue}, {longitudeValue}");
    }

    internal static string Decimal(decimal value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    internal static string Number(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    internal static string DistanceNm(decimal value)
    {
        return $"{Decimal(value)} nm";
    }

    internal static string LengthMeters(decimal value)
    {
        return $"{Decimal(value)} m";
    }

    internal static string DurationHours(int value)
    {
        return $"{Number(value)} h";
    }

    internal static string PortLabel(string name, string? unLocode)
    {
        return string.IsNullOrWhiteSpace(unLocode) ? name : $"{name} ({unLocode})";
    }
}
