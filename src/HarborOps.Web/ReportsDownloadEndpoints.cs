using HarborOps.Web.Api.Reports;

namespace HarborOps.Web;

internal static class ReportsDownloadEndpoints
{
    internal static IEndpointRouteBuilder MapReportsDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports/download").RequireAuthorization("AccessReports");

        group.MapGet("/documents-expiry", DownloadDocumentsExpiryAsync);
        group.MapGet("/compliance-exposure", DownloadComplianceExposureAsync);
        group.MapGet("/maintenance-backlog", DownloadMaintenanceBacklogAsync);

        return app;
    }

    private static async Task<IResult> DownloadDocumentsExpiryAsync(
        HttpContext context, ReportsApiClient reportsApi, CancellationToken cancellationToken)
    {
        if (!TryReadAsOf(context, out var asOf))
        {
            return Results.BadRequest("The asOf date is required.");
        }

        var query = new DocumentsExpiryReportQuery(
            asOf,
            ReadGuid(context, "vesselId"),
            ReadInt(context, "documentTypeId"),
            ReadDateOnly(context, "from"),
            ReadDateOnly(context, "to"),
            ReadString(context, "status"),
            PageNumber: 1,
            PageSize: 1);

        using var response = await reportsApi.ExportDocumentsExpiryAsync(query, cancellationToken);

        return await BufferAsFileOrProblemAsync(response, cancellationToken);
    }

    private static async Task<IResult> DownloadComplianceExposureAsync(
        HttpContext context, ReportsApiClient reportsApi, CancellationToken cancellationToken)
    {
        if (!TryReadAsOf(context, out var asOf))
        {
            return Results.BadRequest("The asOf date is required.");
        }

        var query = new ComplianceExposureReportQuery(
            asOf,
            ReadGuid(context, "vesselId"),
            ReadString(context, "category"),
            PageNumber: 1,
            PageSize: 1);

        using var response = await reportsApi.ExportComplianceExposureAsync(query, cancellationToken);

        return await BufferAsFileOrProblemAsync(response, cancellationToken);
    }

    private static async Task<IResult> DownloadMaintenanceBacklogAsync(
        HttpContext context, ReportsApiClient reportsApi, CancellationToken cancellationToken)
    {
        if (!TryReadAsOf(context, out var asOf))
        {
            return Results.BadRequest("The asOf date is required.");
        }

        var query = new MaintenanceBacklogReportQuery(
            asOf,
            ReadGuid(context, "vesselId"),
            ReadString(context, "priority"),
            ReadString(context, "status"),
            ReadBool(context, "overdue"),
            ReadDateOnly(context, "dueFrom"),
            ReadDateOnly(context, "dueTo"),
            PageNumber: 1,
            PageSize: 1);

        using var response = await reportsApi.ExportMaintenanceBacklogAsync(query, cancellationToken);

        return await BufferAsFileOrProblemAsync(response, cancellationToken);
    }

    private static async Task<IResult> BufferAsFileOrProblemAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return Results.Text(body, contentType, statusCode: (int)response.StatusCode);
        }

        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "report.csv";
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        return Results.File(bytes, contentType, fileName);
    }

    private static bool TryReadAsOf(HttpContext context, out DateOnly asOf)
    {
        var raw = ReadString(context, "asOf");

        return DateOnly.TryParse(raw, out asOf);
    }

    private static string? ReadString(HttpContext context, string key) =>
        context.Request.Query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : null;

    private static Guid? ReadGuid(HttpContext context, string key) =>
        Guid.TryParse(ReadString(context, key), out var value) ? value : null;

    private static int? ReadInt(HttpContext context, string key) =>
        int.TryParse(ReadString(context, key), out var value) ? value : null;

    private static bool? ReadBool(HttpContext context, string key) =>
        bool.TryParse(ReadString(context, key), out var value) ? value : null;

    private static DateOnly? ReadDateOnly(HttpContext context, string key) =>
        DateOnly.TryParse(ReadString(context, key), out var value) ? value : null;
}
