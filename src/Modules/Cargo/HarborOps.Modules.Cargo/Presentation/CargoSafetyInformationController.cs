using System.Security.Claims;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Cargo.Presentation;

[ApiController]
[Route("api/cargo/bookings/{bookingId:guid}/safety-information")]
public sealed class CargoSafetyInformationController(
    GetCargoSafetyInformationQuery getCargoSafetyInformationQuery,
    UpdateCargoSafetyInfoService updateCargoSafetyInfoService,
    SubmitCargoSafetyInfoService submitCargoSafetyInfoService,
    ReviewCargoSafetyInfoService reviewCargoSafetyInfoService)
    : ControllerBase
{
    private const string ManageCargoPolicy = "cargo.manage";
    private const string ReadCargoPolicy = "cargo.read";

    [HttpGet]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<GetCargoSafetyInformationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GetCargoSafetyInformationResponse>> Get(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getCargoSafetyInformationQuery.GetAsync(bookingId, cancellationToken);

            return Ok(response);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (CargoSafetyInfoNotFoundException exception)
        {
            return DeclarationNotFound(exception);
        }
        catch (CargoBookingNotHazardousException exception)
        {
            return NotHazardous(exception);
        }
    }

    [HttpPut]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<CargoSafetyInfoDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargoSafetyInfoDetails>> Update(
        Guid bookingId,
        [FromBody] UpdateCargoSafetyInfoRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await updateCargoSafetyInfoService.UpdateAsync(
                bookingId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (CargoSafetyInfoValidationException exception)
        {
            return ValidationFailed(exception);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (CargoBookingNotHazardousException exception)
        {
            return NotHazardous(exception);
        }
        catch (CargoSafetyInfoTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
    }

    [HttpPost("submit")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<CargoSafetyInfoDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargoSafetyInfoDetails>> Submit(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var submittedByUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await submitCargoSafetyInfoService.SubmitAsync(
                bookingId,
                submittedByUserId,
                cancellationToken);

            return Ok(response);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (CargoSafetyInfoNotFoundException exception)
        {
            return DeclarationNotFound(exception);
        }
        catch (CargoBookingNotHazardousException exception)
        {
            return NotHazardous(exception);
        }
        catch (CargoSafetyInfoTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
    }

    [HttpPost("review")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<CargoSafetyInfoDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargoSafetyInfoDetails>> Review(
        Guid bookingId,
        [FromBody] ReviewCargoSafetyInfoRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var reviewedByUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await reviewCargoSafetyInfoService.ReviewAsync(
                bookingId,
                reviewedByUserId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (CargoSafetyInfoValidationException exception)
        {
            return ValidationFailed(exception);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (CargoSafetyInfoNotFoundException exception)
        {
            return DeclarationNotFound(exception);
        }
        catch (CargoBookingNotHazardousException exception)
        {
            return NotHazardous(exception);
        }
        catch (CargoSafetyInfoTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
    }

    private ActionResult ValidationFailed(CargoSafetyInfoValidationException exception)
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

    private ActionResult BookingNotFound(CargoBookingNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Cargo booking not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult DeclarationNotFound(CargoSafetyInfoNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Cargo safety information not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult NotHazardous(CargoBookingNotHazardousException exception)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Cargo booking is not hazardous.",
            Detail = exception.Message,
        });
    }

    private ActionResult TransitionNotAllowed(CargoSafetyInfoTransitionException exception)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Cargo safety information transition is not allowed.",
            Detail = exception.Message,
        });
    }
}
