using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Voyages.Presentation;

[ApiController]
[Route("api/voyages")]
public sealed class VoyagesController(ScheduleVoyageService scheduleVoyageService) : ControllerBase
{
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
