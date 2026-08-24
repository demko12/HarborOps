using System.Globalization;
using HarborOps.Api.Composition.Reports.Csv;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Api.Composition.Reports.MaintenanceBacklog;

[ApiController]
[Authorize(Permissions.Reports.Read)]
[Authorize(Permissions.Maintenance.Read)]
[Route("api/reports/maintenance-backlog")]
public sealed class MaintenanceBacklogReportController(MaintenanceBacklogReportService reportService)
    : ControllerBase
{
    private static readonly string[] Header =
    [
        "WorkOrderId", "VesselId", "Title", "Priority", "Status", "DueUtc",
        "IsOverdue", "AssignedEngineerId", "CreatedUtc", "CompletedUtc",
    ];

    [HttpGet]
    [ProducesResponseType<MaintenanceBacklogReportPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MaintenanceBacklogReportPage>> Get(
        [FromQuery] DateOnly? asOf,
        [FromQuery] Guid? vesselId,
        [FromQuery] string? priority,
        [FromQuery] string? status,
        [FromQuery] bool? overdue,
        [FromQuery] DateOnly? dueFrom,
        [FromQuery] DateOnly? dueTo,
        CancellationToken cancellationToken,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25)
    {
        try
        {
            var page = await reportService.GetPageAsync(
                asOf, vesselId, priority, status, overdue, dueFrom, dueTo, pageNumber, pageSize, cancellationToken);

            return Ok(page);
        }
        catch (MaintenanceBacklogReportValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
    }

    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export(
        [FromQuery] DateOnly? asOf,
        [FromQuery] Guid? vesselId,
        [FromQuery] string? priority,
        [FromQuery] string? status,
        [FromQuery] bool? overdue,
        [FromQuery] DateOnly? dueFrom,
        [FromQuery] DateOnly? dueTo,
        CancellationToken cancellationToken)
    {
        (DateOnly AsOf, IReadOnlyList<MaintenanceBacklogReportRow> Rows) export;

        try
        {
            export = await reportService.GetExportRowsAsync(
                asOf, vesselId, priority, status, overdue, dueFrom, dueTo, cancellationToken);
        }
        catch (MaintenanceBacklogReportValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"maintenance-backlog-{export.AsOf:yyyy-MM-dd}.csv\"";

        await CsvWriter.WriteAsync(Response.Body, Header, export.Rows.Select(FormatRow), cancellationToken);

        return new EmptyResult();
    }

    private static HttpValidationProblemDetails ValidationProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        return new HttpValidationProblemDetails(errors.ToDictionary(
            error => error.Key,
            error => error.Value,
            StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };
    }

    private static string[] FormatRow(MaintenanceBacklogReportRow row) =>
    [
        row.WorkOrderId.ToString(),
        row.VesselId.ToString(),
        row.Title,
        row.Priority,
        row.Status,
        row.DueUtc.ToString("O", CultureInfo.InvariantCulture),
        row.IsOverdue.ToString(CultureInfo.InvariantCulture),
        row.AssignedEngineerId?.ToString() ?? string.Empty,
        row.CreatedUtc.ToString("O", CultureInfo.InvariantCulture),
        row.CompletedUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
    ];
}
