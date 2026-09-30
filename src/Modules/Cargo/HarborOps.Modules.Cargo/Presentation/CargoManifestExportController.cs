using System.Globalization;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Cargo.Presentation;

[ApiController]
[Route("api/cargo/bookings/{bookingId:guid}/containers")]
[Authorize("cargo.read")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CargoManifestExportController(GetCargoManifestExportQuery getCargoManifestExportQuery)
    : ControllerBase
{
    [HttpGet("export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(
        Guid bookingId,
        [FromQuery] ExportCargoContainersRequest request,
        CancellationToken cancellationToken)
    {
        var result = await GetAsync(bookingId, request, cancellationToken);

        if (result.Export is not { } export)
        {
            return result.Failure!;
        }

        Response.Headers[CargoManifestCsv.MatchingCountHeader] = export.MatchingCount.ToString(CultureInfo.InvariantCulture);

        return File(
            CargoManifestCsv.Write(export),
            CargoManifestCsv.ContentType,
            CargoManifestCsv.FileName(export));
    }

    [HttpGet("print")]
    [ProducesResponseType<CargoManifestExport>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CargoManifestExport>> Print(
        Guid bookingId,
        [FromQuery] ExportCargoContainersRequest request,
        CancellationToken cancellationToken)
    {
        var result = await GetAsync(bookingId, request, cancellationToken);

        return result.Export is { } export ? Ok(export) : result.Failure!;
    }

    private async Task<(CargoManifestExport? Export, ActionResult? Failure)> GetAsync(
        Guid bookingId,
        ExportCargoContainersRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await getCargoManifestExportQuery.GetAsync(bookingId, request, cancellationToken), null);
        }
        catch (GetCargoContainersValidationException exception)
        {
            return (null, ValidationFailed(exception.Errors));
        }
        catch (CargoManifestExportLimitExceededException exception)
        {
            return (null, ValidationFailed(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["limit"] = [exception.Message],
            }));
        }
        catch (CargoBookingNotFoundException exception)
        {
            return (null, NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Cargo booking not found.",
                Detail = exception.Message,
            }));
        }
    }

    private BadRequestObjectResult ValidationFailed(IReadOnlyDictionary<string, string[]> errors)
    {
        return BadRequest(new HttpValidationProblemDetails(
            errors.ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        });
    }
}
