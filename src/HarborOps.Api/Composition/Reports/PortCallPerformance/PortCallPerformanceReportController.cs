using System.Globalization;
using HarborOps.Api.Composition.Reports.Csv;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Api.Composition.Reports.PortCallPerformance;

[ApiController]
[Authorize(Permissions.Reports.Read)]
[Authorize(Permissions.Voyages.Read)]
[Route("api/reports/port-call-performance")]
public sealed class PortCallPerformanceReportController(PortCallPerformanceReportService reportService)
    : ControllerBase
{
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private const string HoursFormat = "0.00";

    private static readonly string[] Header =
    [
        "VoyageStopId", "VoyageId", "VesselId", "PortId", "VoyageNumber", "StopOrder",
        "Port", "PortUnLocode", "Berth", "UsesLiveValues",
        "ActualArrivalUtc", "ActualDepartureUtc", "StatementAvailability", "StatementId", "FinalizedAtUtc",
        "TurnaroundMinutes", "TurnaroundHours", "TurnaroundQuality", "TurnaroundMilestone",
        "ArrivalToAllFastMinutes", "ArrivalToAllFastHours", "ArrivalToAllFastQuality", "ArrivalToAllFastMilestone",
        "CargoElapsedMinutes", "CargoElapsedHours", "CargoElapsedQuality", "CargoElapsedMilestone",
        "CargoCompletedToDepartureMinutes", "CargoCompletedToDepartureHours",
        "CargoCompletedToDepartureQuality", "CargoCompletedToDepartureMilestone",
        "RecordedDelayCount", "RecordedDelayMinutes", "RecordedDelayHours",
    ];

    [HttpGet]
    [ProducesResponseType<PortCallPerformanceReportPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PortCallPerformanceReportPage>> Get(
        [FromQuery] PortCallPerformanceReportFilter filter,
        CancellationToken cancellationToken,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25)
    {
        try
        {
            var page = await reportService.GetPageAsync(filter, pageNumber, pageSize, cancellationToken);

            return Ok(page);
        }
        catch (PortCallPerformanceReportValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
    }

    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export(
        [FromQuery] PortCallPerformanceReportFilter filter,
        CancellationToken cancellationToken)
    {
        (DateOnly DepartedFrom, DateOnly DepartedTo, IReadOnlyList<PortCallPerformanceReportRow> Rows) export;

        try
        {
            export = await reportService.GetExportRowsAsync(filter, cancellationToken);
        }
        catch (PortCallPerformanceReportValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"port-call-performance-{export.DepartedFrom:yyyy-MM-dd}-to-{export.DepartedTo:yyyy-MM-dd}.csv\"";

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

    private static string[] FormatRow(PortCallPerformanceReportRow row) =>
    [
        row.VoyageStopId.ToString(),
        row.VoyageId.ToString(),
        row.VesselId.ToString(),
        row.PortId.ToString(),
        row.VoyageNumber,
        row.StopOrder.ToString(CultureInfo.InvariantCulture),
        row.PortName,
        row.PortUnLocode ?? string.Empty,
        row.BerthName ?? string.Empty,
        row.UsesLiveValues.ToString(CultureInfo.InvariantCulture),
        FormatUtc(row.ActualArrivalUtc),
        FormatUtc(row.ActualDepartureUtc),
        row.StatementAvailability,
        row.StatementId?.ToString() ?? string.Empty,
        FormatUtc(row.FinalizedAtUtc),
        .. FormatMetric(row.Turnaround),
        .. FormatMetric(row.ArrivalToAllFast),
        .. FormatMetric(row.CargoElapsed),
        .. FormatMetric(row.CargoCompletedToDeparture),
        row.RecordedDelayCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        row.RecordedDelayMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        row.RecordedDelayHours?.ToString(HoursFormat, CultureInfo.InvariantCulture) ?? string.Empty,
    ];

    private static string[] FormatMetric(PortCallPerformanceReportMetric? metric) =>
    [
        metric?.Minutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        metric?.Hours?.ToString(HoursFormat, CultureInfo.InvariantCulture) ?? string.Empty,
        metric?.Quality ?? string.Empty,
        metric?.Milestone ?? string.Empty,
    ];

    private static string FormatUtc(DateTime? value) =>
        value is { } utc ? utc.ToString(UtcFormat, CultureInfo.InvariantCulture) : string.Empty;
}
