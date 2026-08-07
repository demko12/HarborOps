using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/assignments")]
public sealed class CrewAssignmentsController(RegisterCrewAssignmentService registerCrewAssignmentService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterCrewAssignmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterCrewAssignmentResponse>> Register(
        [FromBody] RegisterCrewAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerCrewAssignmentService.RegisterAsync(request, cancellationToken);

            return Created($"/api/crew/assignments/{response.Id}", response);
        }
        catch (RegisterCrewAssignmentValidationException exception)
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
        catch (CrewMemberNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Crew member not found.",
                Detail = exception.Message,
            });
        }
        catch (CrewPositionNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Crew position not found.",
                Detail = exception.Message,
            });
        }
        catch (OpenCrewAssignmentExistsException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Crew member already has an open assignment.",
                Detail = exception.Message,
            });
        }
    }
}
