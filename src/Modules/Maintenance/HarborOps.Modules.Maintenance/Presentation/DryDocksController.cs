using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/dry-docks")]
public sealed class DryDocksController(
    ScheduleDryDockService scheduleDryDockService,
    GetDryDocksQuery getDryDocksQuery,
    GetDryDockDetailsQuery getDryDockDetailsQuery,
    StartDryDockService startDryDockService,
    CompleteDryDockService completeDryDockService,
    CancelDryDockService cancelDryDockService)
    : ControllerBase
{
    private const string ReadPolicy = "maintenance.read";
    private const string ManagePolicy = "maintenance.manage";

    [HttpPost]
    [ProducesResponseType<ScheduleDryDockResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ScheduleDryDockResponse>> Schedule(
        [FromBody] ScheduleDryDockRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await scheduleDryDockService.ScheduleAsync(request, cancellationToken);

            return Created($"/api/maintenance/dry-docks/{response.Id}", response);
        }
        catch (ScheduleDryDockValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
    }

    [HttpGet]
    [ProducesResponseType<GetDryDocksResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetDryDocksResponse>> GetDryDocks(
        [FromQuery] GetDryDocksRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getDryDocksQuery.GetDryDocksAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetDryDocksValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
    }

    [HttpGet("{dryDockId:guid}")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<GetDryDockDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetDryDockDetailsResponse>> GetDetails(
        Guid dryDockId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getDryDockDetailsQuery.GetDetailsAsync(dryDockId, cancellationToken);

            return Ok(response);
        }
        catch (DryDockNotFoundException exception)
        {
            return NotFound(DryDockNotFound(exception));
        }
    }

    [HttpPost("{dryDockId:guid}/start")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<DryDockTransitionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DryDockTransitionResponse>> Start(
        Guid dryDockId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await startDryDockService.StartAsync(dryDockId, cancellationToken);

            return Ok(response);
        }
        catch (DryDockNotFoundException exception)
        {
            return NotFound(DryDockNotFound(exception));
        }
        catch (DryDockTransitionNotAllowedException exception)
        {
            return Conflict(TransitionNotAllowed(exception));
        }
    }

    [HttpPost("{dryDockId:guid}/complete")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<DryDockTransitionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DryDockTransitionResponse>> Complete(
        Guid dryDockId,
        [FromBody] CompleteDryDockRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await completeDryDockService.CompleteAsync(
                dryDockId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (CompleteDryDockValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (DryDockNotFoundException exception)
        {
            return NotFound(DryDockNotFound(exception));
        }
        catch (DryDockTransitionNotAllowedException exception)
        {
            return Conflict(TransitionNotAllowed(exception));
        }
        catch (DryDockCompletionInvalidException exception)
        {
            return BadRequest(ToValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [CompleteDryDockValidator.EndDateField] = [exception.Message],
            }));
        }
    }

    [HttpPost("{dryDockId:guid}/cancel")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<DryDockTransitionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DryDockTransitionResponse>> Cancel(
        Guid dryDockId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await cancelDryDockService.CancelAsync(dryDockId, cancellationToken);

            return Ok(response);
        }
        catch (DryDockNotFoundException exception)
        {
            return NotFound(DryDockNotFound(exception));
        }
        catch (DryDockTransitionNotAllowedException exception)
        {
            return Conflict(TransitionNotAllowed(exception));
        }
    }

    private static ProblemDetails DryDockNotFound(DryDockNotFoundException exception)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Dry dock not found.",
            Detail = exception.Message,
        };
    }

    private static ProblemDetails TransitionNotAllowed(DryDockTransitionNotAllowedException exception)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Dry dock status transition is not allowed.",
            Detail = exception.Message,
        };
    }

    private static HttpValidationProblemDetails ToValidationProblem(
        IReadOnlyDictionary<string, string[]> errors)
    {
        return new HttpValidationProblemDetails(
            errors.ToDictionary(
                error => error.Key,
                error => error.Value,
                StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };
    }
}
