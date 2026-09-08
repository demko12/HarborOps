using System.Security.Claims;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Presentation;

[ApiController]
[Route("api/cargo/containers/{containerId:guid}")]
public sealed class ContainerControlController(
    GetContainerControlQuery getContainerControlQuery,
    AssignContainerSealService assignContainerSealService,
    BreakContainerSealService breakContainerSealService,
    RecordContainerInspectionService recordContainerInspectionService,
    ReleaseContainerService releaseContainerService,
    HoldContainerService holdContainerService)
    : ControllerBase
{
    private const string ManageCargoPolicy = "cargo.manage";
    private const string ReadCargoPolicy = "cargo.read";

    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    [HttpGet("control")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<GetContainerControlResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetContainerControlResponse>> Get(
        Guid containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getContainerControlQuery.GetAsync(containerId, cancellationToken);

            return Ok(response);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
    }

    [HttpPost("seal")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<ContainerControlDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContainerControlDetails>> AssignSeal(
        Guid containerId,
        [FromBody] AssignContainerSealRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var details = await assignContainerSealService.AssignAsync(
                containerId,
                actorUserId,
                request,
                cancellationToken);

            return Ok(details);
        }
        catch (ContainerControlValidationException exception)
        {
            return ValidationFailed(exception);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (DuplicateActiveSealException exception)
        {
            return ConflictProblem("Seal number is already in use.", exception.Message);
        }
        catch (ContainerControlTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ConcurrentChange();
        }
    }

    [HttpPost("seal/break")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<ContainerControlDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContainerControlDetails>> BreakSeal(
        Guid containerId,
        [FromBody] BreakContainerSealRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var details = await breakContainerSealService.BreakAsync(
                containerId,
                actorUserId,
                request,
                cancellationToken);

            return Ok(details);
        }
        catch (ContainerControlValidationException exception)
        {
            return ValidationFailed(exception);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (ContainerControlTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ConcurrentChange();
        }
    }

    [HttpPost("inspections")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<ContainerControlDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContainerControlDetails>> RecordInspection(
        Guid containerId,
        [FromBody] RecordContainerInspectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var details = await recordContainerInspectionService.RecordAsync(
                containerId,
                actorUserId,
                request,
                cancellationToken);

            return CreatedAtAction(nameof(Get), new { containerId }, details);
        }
        catch (ContainerControlValidationException exception)
        {
            return ValidationFailed(exception);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ConcurrentChange();
        }
    }

    [HttpPost("release")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<ContainerControlDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContainerControlDetails>> Release(
        Guid containerId,
        [FromBody] ReleaseContainerRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var details = await releaseContainerService.ReleaseAsync(
                containerId,
                actorUserId,
                request,
                cancellationToken);

            return Ok(details);
        }
        catch (ContainerControlValidationException exception)
        {
            return ValidationFailed(exception);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (ContainerControlTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ConcurrentChange();
        }
    }

    [HttpPost("hold")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<ContainerControlDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContainerControlDetails>> Hold(
        Guid containerId,
        [FromBody] HoldContainerRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var details = await holdContainerService.HoldAsync(
                containerId,
                actorUserId,
                request,
                cancellationToken);

            return Ok(details);
        }
        catch (ContainerControlValidationException exception)
        {
            return ValidationFailed(exception);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (ContainerControlTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ConcurrentChange();
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException
        {
            Number: UniqueIndexViolation or UniqueConstraintViolation,
        };
    }

    private bool TryGetActorUserId(out Guid actorUserId)
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out actorUserId);
    }

    private ActionResult ValidationFailed(ContainerControlValidationException exception)
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

    private ActionResult ContainerNotFound(ContainerNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Container not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult TransitionNotAllowed(ContainerControlTransitionException exception)
    {
        return ConflictProblem("Container control transition is not allowed.", exception.Message);
    }

    private ActionResult ConcurrentChange()
    {
        return ConflictProblem(
            "Container control was modified concurrently.",
            "The container control was changed by another request. Reload and try again.");
    }

    private ActionResult ConflictProblem(string title, string detail)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = title,
            Detail = detail,
        });
    }
}
