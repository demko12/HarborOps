using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Cargo.Presentation;

[ApiController]
[Route("api/cargo/bookings")]
public sealed class CargoBookingsController(
    RegisterCargoBookingService registerCargoBookingService,
    SearchCargoBookingsQuery searchCargoBookingsQuery,
    GetCargoBookingDetailsQuery getCargoBookingDetailsQuery)
    : ControllerBase
{
    private const string ReadCargoPolicy = "cargo.read";

    [HttpGet("{bookingId:guid}")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<GetCargoBookingDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetCargoBookingDetailsResponse>> GetDetails(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getCargoBookingDetailsQuery.GetDetailsAsync(
                bookingId,
                cancellationToken);

            return Ok(response);
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
    }

    [HttpGet]
    [ProducesResponseType<SearchCargoBookingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchCargoBookingsResponse>> Search(
        [FromQuery] SearchCargoBookingsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await searchCargoBookingsQuery.SearchAsync(
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (SearchCargoBookingsValidationException exception)
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
    }

    [HttpPost]
    [ProducesResponseType<RegisterCargoBookingResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterCargoBookingResponse>> Register(
        [FromBody] RegisterCargoBookingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerCargoBookingService.RegisterAsync(
                request,
                cancellationToken);

            return Created($"/api/cargo/bookings/{response.Id}", response);
        }
        catch (RegisterCargoBookingValidationException exception)
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
        catch (DuplicateBookingNumberException exception)
        {
            return Conflict(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterCargoBookingValidator.BookingNumberField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A cargo booking with this booking number already exists.",
            });
        }
    }
}
