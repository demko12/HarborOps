using System.Security.Claims;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/assignments")]
public sealed class CrewAssignmentsController(
    RegisterCrewAssignmentService registerCrewAssignmentService,
    ActivateCrewAssignmentService activateCrewAssignmentService,
    CompleteCrewAssignmentService completeCrewAssignmentService,
    CancelCrewAssignmentService cancelCrewAssignmentService)
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
        catch (CrewMemberUnavailableException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict, "Crew member is unavailable.", exception.Message));
        }
    }

    [HttpPost("{assignmentId:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Activate(Guid assignmentId, CancellationToken cancellationToken)
    {
        try
        {
            await activateCrewAssignmentService.ActivateAsync(assignmentId, cancellationToken);
            return Ok();
        }
        catch (CrewAssignmentNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew assignment not found.", exception.Message));
        }
        catch (InvalidCrewAssignmentTransitionException exception)
        {
            return Conflict(Problem(StatusCodes.Status409Conflict, "Crew assignment cannot be activated.", exception.Message));
        }
        catch (CrewMemberUnavailableException exception)
        {
            return Conflict(Problem(StatusCodes.Status409Conflict, "Crew member is unavailable.", exception.Message));
        }
    }

    [HttpPost("{assignmentId:guid}/complete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(
        Guid assignmentId,
        [FromBody] CompleteCrewAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await completeCrewAssignmentService.CompleteAsync(assignmentId, request, cancellationToken);
            return Ok();
        }
        catch (CrewLifecycleValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (CrewAssignmentNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew assignment not found.", exception.Message));
        }
        catch (InvalidCrewAssignmentTransitionException exception)
        {
            return Conflict(Problem(StatusCodes.Status409Conflict, "Crew assignment cannot be completed.", exception.Message));
        }
        catch (InvalidCrewAssignmentDateException exception)
        {
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "Crew assignment cannot be completed.", exception.Message));
        }
    }

    [HttpPost("{assignmentId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        Guid assignmentId,
        [FromBody] CancelCrewAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await cancelCrewAssignmentService.CancelAsync(
                assignmentId,
                GetActingUserId(),
                request,
                cancellationToken);
            return Ok();
        }
        catch (CrewLifecycleValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (CrewAssignmentNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew assignment not found.", exception.Message));
        }
        catch (InvalidCrewAssignmentTransitionException exception)
        {
            return Conflict(Problem(StatusCodes.Status409Conflict, "Crew assignment cannot be cancelled.", exception.Message));
        }
    }

    private Guid GetActingUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static HttpValidationProblemDetails ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        new(errors.ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };

    private static ProblemDetails Problem(int status, string title, string detail) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
    };
}
