using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Domain;

internal static class VesselStatus
{
    internal const string Active = "Active";
    internal const string EnRoute = "En Route";
    internal const string AtPort = "At Port";
    internal const string Maintenance = "Maintenance";
    internal const string Inactive = "Inactive";

    internal static readonly IReadOnlyList<string> All =
    [
        Active,
        EnRoute,
        AtPort,
        Maintenance,
        Inactive,
    ];

    internal static bool IsValid(string? status) =>
        status is not null && All.Contains(status, StringComparer.Ordinal);
}
