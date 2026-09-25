using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HarborOps.Web.Api.Reports;

internal sealed class ReportsApiClient(HttpClient httpClient)
{
    private const string DocumentsExpiryBasePath = "api/reports/documents-expiry";
    private const string ComplianceExposureBasePath = "api/reports/compliance-exposure";
    private const string MaintenanceBacklogBasePath = "api/reports/maintenance-backlog";
    private const string PortCallPerformanceBasePath = "api/reports/port-call-performance";

    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    internal async Task<ReportsApiResult<DocumentsExpiryReportPage>> GetDocumentsExpiryAsync(
        DocumentsExpiryReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildDocumentsExpiryPath(query, forExport: false),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new ReportsApiResult<DocumentsExpiryReportPage>(
                    null, await ReadFailureAsync(response, "Unable to load the documents expiry report", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<DocumentsExpiryReportPage>(cancellationToken);

            return new ReportsApiResult<DocumentsExpiryReportPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new ReportsApiResult<DocumentsExpiryReportPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ReportsApiResult<DocumentsExpiryReportPage>(null, Unavailable());
        }
    }

    internal async Task<ReportsApiResult<ComplianceExposureReportPage>> GetComplianceExposureAsync(
        ComplianceExposureReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildComplianceExposurePath(query, forExport: false),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new ReportsApiResult<ComplianceExposureReportPage>(
                    null, await ReadFailureAsync(response, "Unable to load the compliance exposure report", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<ComplianceExposureReportPage>(cancellationToken);

            return new ReportsApiResult<ComplianceExposureReportPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new ReportsApiResult<ComplianceExposureReportPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ReportsApiResult<ComplianceExposureReportPage>(null, Unavailable());
        }
    }

    internal async Task<ReportsApiResult<MaintenanceBacklogReportPage>> GetMaintenanceBacklogAsync(
        MaintenanceBacklogReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildMaintenanceBacklogPath(query, forExport: false),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new ReportsApiResult<MaintenanceBacklogReportPage>(
                    null, await ReadFailureAsync(response, "Unable to load the maintenance backlog report", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<MaintenanceBacklogReportPage>(cancellationToken);

            return new ReportsApiResult<MaintenanceBacklogReportPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new ReportsApiResult<MaintenanceBacklogReportPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ReportsApiResult<MaintenanceBacklogReportPage>(null, Unavailable());
        }
    }

    internal async Task<ReportsApiResult<PortCallPerformanceReportPage>> GetPortCallPerformanceAsync(
        PortCallPerformanceReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildPortCallPerformancePath(query, forExport: false),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new ReportsApiResult<PortCallPerformanceReportPage>(
                    null, await ReadFailureAsync(response, "Unable to load the port-call performance report", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<PortCallPerformanceReportPage>(cancellationToken);

            return new ReportsApiResult<PortCallPerformanceReportPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new ReportsApiResult<PortCallPerformanceReportPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ReportsApiResult<PortCallPerformanceReportPage>(null, Unavailable());
        }
    }

    internal Task<HttpResponseMessage> ExportDocumentsExpiryAsync(
        DocumentsExpiryReportQuery query, CancellationToken cancellationToken) =>
        httpClient.GetAsync(
            BuildDocumentsExpiryPath(query, forExport: true),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

    internal Task<HttpResponseMessage> ExportComplianceExposureAsync(
        ComplianceExposureReportQuery query, CancellationToken cancellationToken) =>
        httpClient.GetAsync(
            BuildComplianceExposurePath(query, forExport: true),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

    internal Task<HttpResponseMessage> ExportMaintenanceBacklogAsync(
        MaintenanceBacklogReportQuery query, CancellationToken cancellationToken) =>
        httpClient.GetAsync(
            BuildMaintenanceBacklogPath(query, forExport: true),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

    internal Task<HttpResponseMessage> ExportPortCallPerformanceAsync(
        PortCallPerformanceReportQuery query, CancellationToken cancellationToken) =>
        httpClient.GetAsync(
            BuildPortCallPerformancePath(query, forExport: true),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

    internal static string BuildPortCallPerformanceQueryString(PortCallPerformanceReportQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var extra = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"departedFrom={query.DepartedFrom:yyyy-MM-dd}"),
            string.Create(CultureInfo.InvariantCulture, $"departedTo={query.DepartedTo:yyyy-MM-dd}"),
        };

        if (query.VoyageId is { } voyageId)
        {
            extra.Add($"voyageId={voyageId}");
        }

        if (query.VesselId is { } vesselId)
        {
            extra.Add($"vesselId={vesselId}");
        }

        if (query.PortId is { } portId)
        {
            extra.Add($"portId={portId}");
        }

        if (!string.IsNullOrWhiteSpace(query.StatementAvailability))
        {
            extra.Add($"statementAvailability={Uri.EscapeDataString(query.StatementAvailability)}");
        }

        if (!string.IsNullOrWhiteSpace(query.SortBy))
        {
            extra.Add($"sortBy={Uri.EscapeDataString(query.SortBy)}");
        }

        if (!string.IsNullOrWhiteSpace(query.SortDirection))
        {
            extra.Add($"sortDirection={Uri.EscapeDataString(query.SortDirection)}");
        }

        return string.Join('&', extra);
    }

    private static string BuildPortCallPerformancePath(PortCallPerformanceReportQuery query, bool forExport)
    {
        if (forExport)
        {
            return $"{PortCallPerformanceBasePath}/export?{BuildPortCallPerformanceQueryString(query)}";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{PortCallPerformanceBasePath}?{BuildPortCallPerformanceQueryString(query)}&pageNumber={query.PageNumber}&pageSize={query.PageSize}");
    }

    private static string BuildDocumentsExpiryPath(DocumentsExpiryReportQuery query, bool forExport)
    {
        var extra = new List<string> { $"asOf={query.AsOf:yyyy-MM-dd}" };

        if (query.VesselId is { } vesselId)
        {
            extra.Add($"vesselId={vesselId}");
        }

        if (query.DocumentTypeId is { } documentTypeId)
        {
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"documentTypeId={documentTypeId}"));
        }

        if (query.From is { } from)
        {
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"from={from:yyyy-MM-dd}"));
        }

        if (query.To is { } to)
        {
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"to={to:yyyy-MM-dd}"));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            extra.Add($"status={Uri.EscapeDataString(query.Status)}");
        }

        if (!forExport)
        {
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"pageNumber={query.PageNumber}"));
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"pageSize={query.PageSize}"));
        }

        var basePath = forExport ? $"{DocumentsExpiryBasePath}/export" : DocumentsExpiryBasePath;

        return $"{basePath}?{string.Join('&', extra)}";
    }

    private static string BuildComplianceExposurePath(ComplianceExposureReportQuery query, bool forExport)
    {
        var extra = new List<string> { $"asOf={query.AsOf:yyyy-MM-dd}" };

        if (query.VesselId is { } vesselId)
        {
            extra.Add($"vesselId={vesselId}");
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            extra.Add($"category={Uri.EscapeDataString(query.Category)}");
        }

        if (!forExport)
        {
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"pageNumber={query.PageNumber}"));
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"pageSize={query.PageSize}"));
        }

        var basePath = forExport ? $"{ComplianceExposureBasePath}/export" : ComplianceExposureBasePath;

        return $"{basePath}?{string.Join('&', extra)}";
    }

    private static string BuildMaintenanceBacklogPath(MaintenanceBacklogReportQuery query, bool forExport)
    {
        var extra = new List<string> { $"asOf={query.AsOf:yyyy-MM-dd}" };

        if (query.VesselId is { } vesselId)
        {
            extra.Add($"vesselId={vesselId}");
        }

        if (!string.IsNullOrWhiteSpace(query.Priority))
        {
            extra.Add($"priority={Uri.EscapeDataString(query.Priority)}");
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            extra.Add($"status={Uri.EscapeDataString(query.Status)}");
        }

        if (query.Overdue is { } overdue)
        {
            extra.Add($"overdue={(overdue ? "true" : "false")}");
        }

        if (query.DueFrom is { } dueFrom)
        {
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"dueFrom={dueFrom:yyyy-MM-dd}"));
        }

        if (query.DueTo is { } dueTo)
        {
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"dueTo={dueTo:yyyy-MM-dd}"));
        }

        if (!forExport)
        {
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"pageNumber={query.PageNumber}"));
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"pageSize={query.PageSize}"));
        }

        var basePath = forExport ? $"{MaintenanceBacklogBasePath}/export" : MaintenanceBacklogBasePath;

        return $"{basePath}?{string.Join('&', extra)}";
    }

    private static async Task<ReportsApiFailure> ReadFailureAsync(
        HttpResponseMessage response,
        string defaultTitle,
        CancellationToken cancellationToken)
    {
        ApiProblemDetails? problem = null;

        try
        {
            problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken);
        }
        catch (JsonException)
        {
            // Not every failure carries a problem details body; keep the page retryable.
        }

        return new ReportsApiFailure(
            (int)response.StatusCode,
            problem?.Title ?? (response.StatusCode == HttpStatusCode.Forbidden
                ? "Report access denied"
                : defaultTitle),
            problem?.Detail ?? (response.StatusCode == HttpStatusCode.Forbidden
                ? "Your account does not have permission to access this report."
                : "Please review the filters and try again."),
            problem?.Errors ?? NoErrors);
    }

    private static ReportsApiFailure Unavailable()
    {
        return new ReportsApiFailure(
            0,
            "HarborOps API is unavailable",
            "The reporting service cannot be reached right now.",
            NoErrors);
    }
}
