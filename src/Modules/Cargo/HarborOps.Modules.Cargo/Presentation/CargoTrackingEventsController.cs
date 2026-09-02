using System.Security.Claims;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Cargo.Presentation;

[ApiController]
[Route("api/cargo/bookings/{bookingId:guid}/tracking-events")]
public sealed class CargoTrackingEventsController(
    RecordCargoTrackingEventService recordCargoTrackingEventService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RecordCargoTrackingEventResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordCargoTrackingEventResponse>> Record(
        Guid bookingId,
        [FromBody] RecordCargoTrackingEventRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var responsibleUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await recordCargoTrackingEventService.RecordAsync(
                bookingId,
                responsibleUserId,
                request,
                cancellationToken);

            return Created(
                $"/api/cargo/bookings/{bookingId}/tracking-events/{response.Id}",
                response);
        }
        catch (RecordCargoTrackingEventValidationException exception)
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
        catch (CargoSafetyApprovalRequiredException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = exception.ReviewStatus is null
                    ? "Cargo safety information is required before loading."
                    : "Cargo safety declaration is not approved.",
                Detail = exception.Message,
            });
        }
        catch (CargoTrackingEventTransitionException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Cargo status transition is not allowed.",
                Detail = exception.Message,
            });
        }
    }
}
