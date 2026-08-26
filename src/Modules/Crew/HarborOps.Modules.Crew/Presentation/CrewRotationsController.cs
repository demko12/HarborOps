using System.Security.Claims;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/rotations")]
public sealed class CrewRotationsController(
    IGetCrewRotationsQuery getCrewRotationsQuery,
    IGetCrewRotationQuery getCrewRotationQuery,
    PlanCrewRotationService planCrewRotationService,
    UpdateCrewRotationService updateCrewRotationService,
    ExecuteCrewRotationService executeCrewRotationService,
    CancelCrewRotationService cancelCrewRotationService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetCrewRotationsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetCrewRotationsResponse>> GetRotations(
        [FromQuery] GetCrewRotationsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getCrewRotationsQuery.GetAsync(request, cancellationToken));
        }
        catch (GetCrewRotationsValidationException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(exception.Errors.ToDictionary(
                error => error.Key,
                error => error.Value,
                StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }
    }

    [HttpGet("/api/crew/assignments/{assignmentId:guid}/rotation")]
    [ProducesResponseType<CrewRotationListItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CrewRotationListItem>> GetRotation(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getCrewRotationQuery.GetAsync(assignmentId, cancellationToken));
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
        catch (CrewRotationNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Crew rotation not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("/api/crew/assignments/{assignmentId:guid}/rotation")]
    [ProducesResponseType<PlanCrewRotationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlanCrewRotationResponse>> Plan(
        Guid assignmentId,
        [FromBody] PlanCrewRotationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await planCrewRotationService.PlanAsync(assignmentId, request, cancellationToken);

            return Created($"/api/crew/assignments/{assignmentId}/rotation", response);
        }
        catch (CrewRotationValidationException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(exception.Errors.ToDictionary(
                error => error.Key,
                error => error.Value,
                StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
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
        catch (CrewMemberNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Relieving crew member not found.",
                Detail = exception.Message,
            });
        }
        catch (CrewAssignmentNotEligibleForRotationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Crew assignment is not eligible for a rotation.",
                Detail = exception.Message,
            });
        }
        catch (DuplicateCrewRotationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Crew assignment already has a planned rotation.",
                Detail = exception.Message,
            });
        }
        catch (SelfRelievingCrewMemberException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A crew member cannot relieve themselves.",
                Detail = exception.Message,
            });
        }
        catch (RelievingCrewMemberUnavailableException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Relieving crew member is not available.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPut("/api/crew/assignments/{assignmentId:guid}/rotation")]
    [ProducesResponseType<UpdateCrewRotationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdateCrewRotationResponse>> Update(
        Guid assignmentId,
        [FromBody] UpdateCrewRotationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await updateCrewRotationService.UpdateAsync(assignmentId, request, cancellationToken));
        }
        catch (CrewRotationValidationException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(exception.Errors.ToDictionary(
                error => error.Key,
                error => error.Value,
                StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
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
        catch (CrewRotationNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Crew rotation not found.",
                Detail = exception.Message,
            });
        }
        catch (CrewMemberNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Relieving crew member not found.",
                Detail = exception.Message,
            });
        }
        catch (CrewAssignmentNotEligibleForRotationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Crew assignment is not eligible for a rotation.",
                Detail = exception.Message,
            });
        }
        catch (SelfRelievingCrewMemberException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A crew member cannot relieve themselves.",
                Detail = exception.Message,
            });
        }
        catch (RelievingCrewMemberUnavailableException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Relieving crew member is not available.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("/api/crew/assignments/{assignmentId:guid}/rotation/execute")]
    [ProducesResponseType<ExecuteCrewRotationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExecuteCrewRotationResponse>> Execute(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await executeCrewRotationService.ExecuteAsync(assignmentId, GetActingUserId(), cancellationToken));
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
        catch (CrewRotationNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Crew rotation not found.",
                Detail = exception.Message,
            });
        }
        catch (CrewMemberNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Relieving crew member not found.",
                Detail = exception.Message,
            });
        }
        catch (CrewRotationNotExecutableException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Crew rotation cannot be executed.",
                Detail = exception.Message,
            });
        }
        catch (CrewAssignmentNotEligibleForRotationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Crew assignment is not eligible for a rotation.",
                Detail = exception.Message,
            });
        }
        catch (SelfRelievingCrewMemberException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A crew member cannot relieve themselves.",
                Detail = exception.Message,
            });
        }
        catch (RelievingCrewMemberUnavailableException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Relieving crew member is not available.",
                Detail = exception.Message,
            });
        }
        catch (InvalidCrewRotationTransitionException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Crew rotation cannot be executed.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{rotationId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        Guid rotationId,
        [FromBody] CancelCrewRotationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await cancelCrewRotationService.CancelAsync(rotationId, GetActingUserId(), request, cancellationToken);
            return Ok();
        }
        catch (CrewLifecycleValidationException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(exception.Errors.ToDictionary(
                error => error.Key,
                error => error.Value,
                StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }
        catch (CrewRotationNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Crew rotation not found.",
                Detail = exception.Message,
            });
        }
        catch (InvalidCrewRotationTransitionException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Crew rotation cannot be cancelled.",
                Detail = exception.Message,
            });
        }
    }

    private Guid GetActingUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
