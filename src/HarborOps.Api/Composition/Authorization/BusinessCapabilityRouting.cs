namespace HarborOps.Api.Composition.Authorization;



internal static class BusinessCapabilityRouting
{
    public static readonly IReadOnlyList<(string RoutePrefix, string Capability)> Map =
    [
        ("/api/fleet", "fleet"),
        ("/api/crew", "crew"),
        ("/api/voyages", "voyages"),
        ("/api/cargo", "cargo"),
        ("/api/maintenance", "maintenance"),
        ("/api/compliance", "compliance"),
        ("/api/documents", "documents"),
    ];
}
