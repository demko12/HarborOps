namespace HarborOps.Web.Components.Pages.Reports;

public partial class Reports
{
    internal const int PageSize = 25;

    internal const string DocumentsExpiryUrl = "/reports/documents-expiry";
    internal const string ComplianceExposureUrl = "/reports/compliance-exposure";
    internal const string MaintenanceBacklogUrl = "/reports/maintenance-backlog";
    internal const string PortCallPerformanceUrl = "/reports/port-call-performance";

    private string? FirstAccessibleReportUrl =>
        AuthorizationState.CanAccessDocuments ? DocumentsExpiryUrl
        : AuthorizationState.CanAccessCompliance ? ComplianceExposureUrl
        : AuthorizationState.CanAccessMaintenance ? MaintenanceBacklogUrl
        : AuthorizationState.CanAccessVoyages ? PortCallPerformanceUrl
        : null;

    protected override void OnInitialized()
    {
        if (FirstAccessibleReportUrl is { } url)
        {
            Navigation.NavigateTo(url, replace: true);
        }
    }

    internal static string? ValidateRange(DateOnly? from, DateOnly? to) =>
        from is { } fromDate && to is { } toDate && toDate < fromDate
            ? "The \"to\" date must be on or after the \"from\" date."
            : null;
}
