namespace HarborOps.Modules.Voyages.Domain;

internal static class PortCallActivityType
{
    internal const int MaxLength = 30;

    internal const string PilotOnBoard = "PilotOnBoard";
    internal const string AllFast = "AllFast";
    internal const string CargoStarted = "CargoStarted";
    internal const string CargoCompleted = "CargoCompleted";
    internal const string OperationalNote = "OperationalNote";

    internal static readonly IReadOnlyList<string> All =
    [
        PilotOnBoard,
        AllFast,
        CargoStarted,
        CargoCompleted,
        OperationalNote,
    ];

    internal static bool IsValid(string? activityType) =>
        activityType is not null && All.Contains(activityType, StringComparer.Ordinal);
}
