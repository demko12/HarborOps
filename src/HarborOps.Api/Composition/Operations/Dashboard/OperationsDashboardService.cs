using System.Security.Claims;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Api.Composition.Operations.Dashboard;

public sealed class OperationsDashboardService(
    IOperationsDashboardFleetQuery fleetQuery,
    IOperationsDashboardCrewQuery crewQuery,
    IOperationsDashboardVoyagesQuery voyagesQuery,
    IOperationsDashboardCargoQuery cargoQuery,
    IOperationsDashboardMaintenanceQuery maintenanceQuery,
    IOperationsDashboardComplianceQuery complianceQuery,
    IOperationsDashboardDocumentsQuery documentsQuery)
{
    private const int MaximumItemsPerSection = 10;
    private const int MaximumPriorityItems = 5;
    private const int AttentionWindowDays = 30;
    private const int VoyageWindowDays = 7;

    public async Task<OperationsDashboardResponse> GetAsync(
        ClaimsPrincipal user,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var sections = new List<OperationsDashboardSectionResponse>();
        var asOfUtc = DateTime.SpecifyKind(asOf.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var attentionThrough = asOf.AddDays(AttentionWindowDays);

        if (HasReadPermission(user, Permissions.Fleet.Read))
        {
            sections.Add(await GetFleetSectionAsync(cancellationToken));
        }

        if (HasReadPermission(user, Permissions.Crew.Read))
        {
            sections.Add(await GetCrewSectionAsync(asOf, attentionThrough, cancellationToken));
        }

        if (HasReadPermission(user, Permissions.Voyages.Read))
        {
            sections.Add(await GetVoyagesSectionAsync(asOfUtc, asOfUtc.AddDays(VoyageWindowDays), cancellationToken));
        }

        if (HasReadPermission(user, Permissions.Cargo.Read))
        {
            sections.Add(await GetCargoSectionAsync(cancellationToken));
        }

        if (HasReadPermission(user, Permissions.Maintenance.Read))
        {
            sections.Add(await GetMaintenanceSectionAsync(asOfUtc, cancellationToken));
        }

        if (HasReadPermission(user, Permissions.Compliance.Read))
        {
            sections.Add(await GetComplianceSectionAsync(attentionThrough, cancellationToken));
        }

        if (HasReadPermission(user, Permissions.Documents.Read))
        {
            sections.Add(await GetDocumentsSectionAsync(attentionThrough, cancellationToken));
        }

        return new OperationsDashboardResponse(asOf, sections, BuildPriorityItems(sections));
    }

    private async Task<OperationsDashboardSectionResponse> GetFleetSectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await fleetQuery.GetActiveVesselsAsync(MaximumItemsPerSection, cancellationToken);
            return Section("fleet", "Fleet", "Active vessels", result.ActiveVesselCount, "/fleet?status=Active",
                result.Items.Select(item => Item(
                    item.Name,
                    item.CurrentStatus,
                    null,
                    "normal",
                    $"/fleet/{item.VesselId}")));
        }
        catch (Exception exception) when (IsSectionFailure(exception, cancellationToken))
        {
            return Unavailable("fleet", "Fleet", "Active vessels", "/fleet?status=Active");
        }
    }

    private async Task<OperationsDashboardSectionResponse> GetCrewSectionAsync(DateOnly asOf, DateOnly through, CancellationToken cancellationToken)
    {
        try
        {
            var result = await crewQuery.GetExpiringCredentialsAsync(asOf, through, MaximumItemsPerSection, cancellationToken);
            return Section("crew", "Crew", "Credentials expiring within 30 days", result.ExpiringCredentialCount, "/crew",
                result.Items.Select(item => Item(
                    item.CrewMemberName,
                    $"{item.CredentialName} expires",
                    item.ExpiryDate.ToString("yyyy-MM-dd"),
                    item.ExpiryDate < asOf ? "critical" : "warning",
                    $"/crew/{item.CrewMemberId}")));
        }
        catch (Exception exception) when (IsSectionFailure(exception, cancellationToken))
        {
            return Unavailable("crew", "Crew", "Credentials expiring within 30 days", "/crew");
        }
    }

    private async Task<OperationsDashboardSectionResponse> GetVoyagesSectionAsync(DateTime asOfUtc, DateTime throughUtc, CancellationToken cancellationToken)
    {
        try
        {
            var result = await voyagesQuery.GetUpcomingVoyagesAsync(asOfUtc, throughUtc, MaximumItemsPerSection, cancellationToken);
            return Section("voyages", "Voyages", "Planned or in progress within 7 days", result.UpcomingVoyageCount, "/voyages",
                result.Items.Select(item => Item(
                    item.VoyageNumber,
                    $"{item.DeparturePortCode} to {item.ArrivalPortCode} · {item.Status}",
                    item.PlannedDeparture.ToString("yyyy-MM-dd"),
                    "normal")));
        }
        catch (Exception exception) when (IsSectionFailure(exception, cancellationToken))
        {
            return Unavailable("voyages", "Voyages", "Planned or in progress within 7 days", "/voyages");
        }
    }

    private async Task<OperationsDashboardSectionResponse> GetCargoSectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await cargoQuery.GetOpenHazardousBookingsAsync(MaximumItemsPerSection, cancellationToken);
            return Section("cargo", "Cargo", "Open hazardous bookings", result.OpenHazardousBookingCount, "/cargo?isHazardous=true",
                result.Items.Select(item => Item(
                    item.BookingNumber,
                    $"Hazardous · {item.CargoType} · {item.Status}",
                    null,
                    "warning")));
        }
        catch (Exception exception) when (IsSectionFailure(exception, cancellationToken))
        {
            return Unavailable("cargo", "Cargo", "Open hazardous bookings", "/cargo?isHazardous=true");
        }
    }

    private async Task<OperationsDashboardSectionResponse> GetMaintenanceSectionAsync(DateTime asOfUtc, CancellationToken cancellationToken)
    {
        try
        {
            var result = await maintenanceQuery.GetAttentionWorkOrdersAsync(asOfUtc, MaximumItemsPerSection, cancellationToken);
            return Section("maintenance", "Maintenance", "Overdue or critical open work orders", result.AttentionWorkOrderCount, "/maintenance?overdue=true",
                result.Items.Select(item => Item(
                    item.Title,
                    $"{item.Priority} · {item.Status}",
                    item.DueUtc.ToString("yyyy-MM-dd"),
                    item.IsOverdue || item.Priority == "Critical" ? "critical" : "warning",
                    $"/maintenance/work-orders/{item.WorkOrderId}")));
        }
        catch (Exception exception) when (IsSectionFailure(exception, cancellationToken))
        {
            return Unavailable("maintenance", "Maintenance", "Overdue or critical open work orders", "/maintenance?overdue=true");
        }
    }

    private async Task<OperationsDashboardSectionResponse> GetComplianceSectionAsync(DateOnly through, CancellationToken cancellationToken)
    {
        try
        {
            var result = await complianceQuery.GetAttentionItemsAsync(through, MaximumItemsPerSection, cancellationToken);
            return Section("compliance", "Compliance", "Expiring or revoked certificates and critical findings", result.AttentionItemCount, "/compliance",
                result.Items.Select(item => Item(item.Title, item.ItemType, item.DueDate.ToString("yyyy-MM-dd"), item.Severity == "Critical" ? "critical" : "warning")));
        }
        catch (Exception exception) when (IsSectionFailure(exception, cancellationToken))
        {
            return Unavailable("compliance", "Compliance", "Expiring or revoked certificates and critical findings", "/compliance");
        }
    }

    private async Task<OperationsDashboardSectionResponse> GetDocumentsSectionAsync(DateOnly through, CancellationToken cancellationToken)
    {
        try
        {
            var result = await documentsQuery.GetExpiringDocumentsAsync(through, MaximumItemsPerSection, cancellationToken);
            return Section("documents", "Documents", "Documents expiring within 30 days", result.ExpiringDocumentCount, "/documents",
                result.Items.Select(item => Item(item.DocumentTypeName, item.Number, item.ExpiryDate.ToString("yyyy-MM-dd"), "warning")));
        }
        catch (Exception exception) when (IsSectionFailure(exception, cancellationToken))
        {
            return Unavailable("documents", "Documents", "Documents expiring within 30 days", "/documents");
        }
    }

    private static bool HasReadPermission(ClaimsPrincipal user, string permission) =>
        user.HasClaim(Permissions.ClaimType, permission);

    private static bool IsSectionFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    private static OperationsDashboardSectionResponse Section(
        string capability,
        string title,
        string summaryLabel,
        int count,
        string link,
        IEnumerable<OperationsDashboardAttentionItemResponse> items) =>
        new(capability, title, summaryLabel, count, link, items.Take(MaximumItemsPerSection).ToArray(), false);

    private static OperationsDashboardSectionResponse Unavailable(
        string capability,
        string title,
        string summaryLabel,
        string link) =>
        new(capability, title, summaryLabel, 0, link, [], true);

    private static OperationsDashboardAttentionItemResponse Item(
        string title,
        string detail,
        string? dueLabel,
        string severity,
        string? link = null) =>
        new(title, detail, dueLabel, severity, link);

    private static IReadOnlyList<OperationsDashboardPriorityItemResponse> BuildPriorityItems(
        IReadOnlyList<OperationsDashboardSectionResponse> sections) =>
        sections
            .Where(section => !section.IsUnavailable)
            .SelectMany(section => section.Items.Select(item => new OperationsDashboardPriorityItemResponse(
                section.Capability,
                section.Title,
                item.Title,
                item.Detail,
                item.DueLabel,
                item.Severity,
                item.Link ?? section.Link)))
            .OrderBy(item => SeverityOrder(item.Severity))
            .ThenBy(item => item.DueLabel is null ? DateOnly.MaxValue : DateOnly.Parse(item.DueLabel, System.Globalization.CultureInfo.InvariantCulture))
            .ThenBy(item => item.Capability, StringComparer.Ordinal)
            .ThenBy(item => item.Title, StringComparer.Ordinal)
            .Take(MaximumPriorityItems)
            .ToArray();

    private static int SeverityOrder(string severity) => severity switch
    {
        "critical" => 0,
        "warning" => 1,
        _ => 2,
    };
}
