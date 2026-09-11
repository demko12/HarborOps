namespace HarborOps.Modules.Voyages.Domain;

internal static class PortCallClearanceCategory
{
    internal const int MaxLength = 30;

    internal const string PortAuthority = "PortAuthority";
    internal const string Customs = "Customs";
    internal const string Immigration = "Immigration";
    internal const string Health = "Health";
    internal const string Security = "Security";
    internal const string Other = "Other";

    internal static readonly IReadOnlyList<string> All =
    [
        PortAuthority,
        Customs,
        Immigration,
        Health,
        Security,
        Other,
    ];

    internal static bool IsValid(string? category) =>
        category is not null && All.Contains(category, StringComparer.Ordinal);
}
