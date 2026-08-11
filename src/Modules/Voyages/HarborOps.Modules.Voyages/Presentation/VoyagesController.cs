using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Voyages.Presentation;

[ApiController]
[Route("api/voyages")]
public sealed class VoyagesController(
    ScheduleVoyageService scheduleVoyageService,
    RecordVoyageDepartureService recordVoyageDepartureService,
    RecordVoyageArrivalService recordVoyageArrivalService)
    : ControllerBase
{
    [HttpPost("{voyageId:guid}/departure")]
    [ProducesResponseType<RecordVoyageDepartureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordVoyageDepartureResponse>> RecordDeparture(
        Guid voyageId,
        [FromBody] RecordVoyageDepartureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await recordVoyageDepartureService.RecordDepartureAsync(
                voyageId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (RecordVoyageDepartureValidationException exception)
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
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageNotPlannedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage cannot depart from its current status.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{voyageId:guid}/arrival")]
    [ProducesResponseType<RecordVoyageArrivalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordVoyageArrivalResponse>> RecordArrival(
        Guid voyageId,
        [FromBody] RecordVoyageArrivalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await recordVoyageArrivalService.RecordArrivalAsync(
                voyageId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (RecordVoyageArrivalValidationException exception)
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
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageNotInProgressException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage cannot arrive from its current status.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost]
    [ProducesResponseType<ScheduleVoyageResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScheduleVoyageResponse>> Schedule(
        [FromBody] ScheduleVoyageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await scheduleVoyageService.ScheduleAsync(request, cancellationToken);

            return Created($"/api/voyages/{response.Id}", response);
        }
        catch (ScheduleVoyageValidationException exception)
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
        catch (DuplicateVoyageNumberException exception)
        {
            return Conflict(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [ScheduleVoyageValidator.VoyageNumberField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A voyage with this voyage number already exists.",
            });
        }
    }
}
