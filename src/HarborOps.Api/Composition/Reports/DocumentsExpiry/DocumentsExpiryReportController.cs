using System.Globalization;
using HarborOps.Api.Composition.Reports.Csv;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Api.Composition.Reports.DocumentsExpiry;

[ApiController]
[Authorize(Permissions.Reports.Read)]
[Authorize(Permissions.Documents.Read)]
[Route("api/reports/documents-expiry")]
public sealed class DocumentsExpiryReportController(DocumentsExpiryReportService reportService)
    : ControllerBase
{
    private static readonly string[] Header =
    [
        "DocumentId", "VesselId", "DocumentTypeId", "DocumentTypeName", "Number",
        "IssueDate", "ExpiryDate", "IssuingAuthority", "Status", "DaysUntilExpiry", "IsExpired",
    ];

    [HttpGet]
    [ProducesResponseType<DocumentsExpiryReportPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DocumentsExpiryReportPage>> Get(
        [FromQuery] DateOnly? asOf,
        [FromQuery] Guid? vesselId,
        [FromQuery] int? documentTypeId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? status,
        CancellationToken cancellationToken,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25)
    {
        try
        {
            var page = await reportService.GetPageAsync(
                asOf, vesselId, documentTypeId, from, to, status, pageNumber, pageSize, cancellationToken);

            return Ok(page);
        }
        catch (DocumentsExpiryReportValidationException exception)
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
        [FromQuery] int? documentTypeId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        (DateOnly AsOf, IReadOnlyList<DocumentsExpiryReportRow> Rows) export;

        try
        {
            export = await reportService.GetExportRowsAsync(
                asOf, vesselId, documentTypeId, from, to, status, cancellationToken);
        }
        catch (DocumentsExpiryReportValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"documents-expiry-{export.AsOf:yyyy-MM-dd}.csv\"";

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

    private static string[] FormatRow(DocumentsExpiryReportRow row) =>
    [
        row.DocumentId.ToString(),
        row.VesselId.ToString(),
        row.DocumentTypeId.ToString(CultureInfo.InvariantCulture),
        row.DocumentTypeName,
        row.Number,
        row.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        row.ExpiryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        row.IssuingAuthority,
        row.Status,
        row.DaysUntilExpiry.ToString(CultureInfo.InvariantCulture),
        row.IsExpired.ToString(CultureInfo.InvariantCulture),
    ];
}
