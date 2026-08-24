using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/assignments")]
public sealed class CrewAssignmentsController(
    RegisterCrewAssignmentService registerCrewAssignmentService,
    DeleteCrewAssignmentService deleteCrewAssignmentService)
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

    [HttpDelete("{assignmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid assignmentId, CancellationToken cancellationToken)
    {
        try
        {
            await deleteCrewAssignmentService.DeleteAsync(assignmentId, cancellationToken);

            return NoContent();
        }
        catch (CrewAssignmentNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Crew assignment not found.",
                Detail = exception.Message,
            });
        }
        catch (CrewAssignmentNotDeletableException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Crew assignment cannot be deleted.",
                Detail = exception.Message,
            });
        }
    }
}
