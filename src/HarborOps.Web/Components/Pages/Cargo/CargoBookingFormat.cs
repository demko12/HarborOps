using System.Globalization;

namespace HarborOps.Web.Components.Pages.Cargo;


internal static class CargoBookingFormat
{
    internal const string HazardousLabel = "Hazardous";
    internal const string NotHazardousLabel = "Not hazardous";

    internal static readonly IReadOnlyList<string> Statuses =
    [
        "Registered",
        "Planned",
        "Loaded",
        "InTransit",
        "Arrived",
        "Unloaded",
        "Completed",
    ];

    internal static string WeightKg(decimal weightKg)
    {
        return weightKg.ToString("N3", CultureInfo.InvariantCulture);
    }

    internal static string PackageCount(int packageCount)
    {
        return packageCount.ToString("N0", CultureInfo.InvariantCulture);
    }

    internal static string CreatedUtc(DateTime createdUtc)
    {
        return createdUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    internal static string CreatedUtcDate(DateTime createdUtc)
    {
        return createdUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal static string CreatedUtcTime(DateTime createdUtc)
    {
        return createdUtc.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    internal static string HazardLabel(bool isHazardous)
    {
        return isHazardous ? HazardousLabel : NotHazardousLabel;
    }

    internal static string ShortVoyage(Guid voyageId)
    {
        var text = voyageId.ToString("D", CultureInfo.InvariantCulture);

        return $"{text[..8]}…{text[^4..]}";
    }

    internal static int LifecycleStep(string status)
    {
        var index = Statuses
            .Select((name, position) => (name, position))
            .FirstOrDefault(entry => string.Equals(entry.name, status, StringComparison.OrdinalIgnoreCase))
            .position;

        return Statuses.Any(name => string.Equals(name, status, StringComparison.OrdinalIgnoreCase))
            ? index + 1
            : 0;
    }
}
