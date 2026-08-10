using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Cargo.Presentation;

[ApiController]
[Route("api/cargo/bookings/{bookingId:guid}/safety-information")]
public sealed class CargoSafetyInformationController(
    DeclareCargoSafetyInfoService declareCargoSafetyInfoService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<DeclareCargoSafetyInfoResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeclareCargoSafetyInfoResponse>> Declare(
        Guid bookingId,
        [FromBody] DeclareCargoSafetyInfoRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await declareCargoSafetyInfoService.DeclareAsync(
                bookingId,
                request,
                cancellationToken);

            return Created(
                $"/api/cargo/bookings/{bookingId}/safety-information",
                response);
        }
        catch (DeclareCargoSafetyInfoValidationException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(
                exception.Errors.ToDictionary(
                    error => error.Key,
                    error => error.Value,
                    StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }
        catch (CargoBookingNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Cargo booking not found.",
                Detail = exception.Message,
            });
        }
        catch (CargoBookingNotHazardousException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Cargo booking is not hazardous.",
                Detail = exception.Message,
            });
        }
        catch (CargoSafetyInfoAlreadyDeclaredException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Cargo booking already has safety information.",
                Detail = exception.Message,
            });
        }
    }
}
