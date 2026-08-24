namespace HarborOps.Modules.Voyages.Domain;

internal static class BerthStatus
{
    internal const string Available = "Available";
    internal const string Occupied = "Occupied";
    internal const string Reserved = "Reserved";
    internal const string OutOfService = "Out of service";

    internal static readonly IReadOnlyList<string> All =
    [
        Available,
        Occupied,
        Reserved,
        OutOfService,
    ];

    internal static bool IsValid(string? status) =>
        status is not null && All.Contains(status, StringComparer.Ordinal);
}
