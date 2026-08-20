using System.Globalization;
using HarborOps.Api.Composition.Reports.Csv;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Api.Composition.Reports.ComplianceExposure;

[ApiController]
[Authorize(Permissions.Reports.Read)]
[Authorize(Permissions.Compliance.Read)]
[Route("api/reports/compliance-exposure")]
public sealed class ComplianceExposureReportController(ComplianceExposureReportService reportService)
    : ControllerBase
{
    private static readonly string[] Header =
    ["ItemType", "VesselId", "Reference", "Description", "RelevantDate", "Status", "Severity"];

    [HttpGet]
    [ProducesResponseType<ComplianceExposureReportPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ComplianceExposureReportPage>> Get(
        [FromQuery] DateOnly? asOf,
        [FromQuery] Guid? vesselId,
        [FromQuery] string? category,
        CancellationToken cancellationToken,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25)
    {
        try
        {
            var page = await reportService.GetPageAsync(asOf, vesselId, category, pageNumber, pageSize, cancellationToken);

            return Ok(page);
        }
        catch (ComplianceExposureReportValidationException exception)
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
        [FromQuery] string? category,
        CancellationToken cancellationToken)
    {
        (DateOnly AsOf, IReadOnlyList<ComplianceExposureReportRow> Rows) export;

        try
        {
            export = await reportService.GetExportRowsAsync(asOf, vesselId, category, cancellationToken);
        }
        catch (ComplianceExposureReportValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"compliance-exposure-{export.AsOf:yyyy-MM-dd}.csv\"";

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

    private static string[] FormatRow(ComplianceExposureReportRow row) =>
    [
        row.ItemType,
        row.VesselId.ToString(),
        row.Reference,
        row.Description,
        row.RelevantDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        row.Status,
        row.Severity ?? string.Empty,
    ];
}
