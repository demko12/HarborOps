using System.Security.Claims;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Api.Composition.Voyages;

[ApiController]
[Authorize]
[Route("api/voyages")]
public sealed class VoyageEstimateRevisionsController(VoyageEstimateRevisionService service)
    : ControllerBase
{
    [HttpPost("{voyageId:guid}/delays/{delayId:guid}/apply-impact")]
    [ProducesResponseType<VoyageEstimateRevisionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VoyageEstimateRevisionResult>> ApplyImpact(
        Guid voyageId,
        Guid delayId,
        [FromBody] ApplyDelayImpactRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ApplyAsync(
                voyageId,
                delayId,
                GetActingUserId(),
                request,
                cancellationToken);

            return Ok(result);
        }
        catch (ApplyDelayImpactValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return NotFound(ToProblem("The requested voyage was not found.", exception));
        }
        catch (DelayNotFoundException exception)
        {
            return NotFound(ToProblem("The requested delay was not found.", exception));
        }
        catch (VoyageStopNotFoundException exception)
        {
            return NotFound(ToProblem("The requested voyage stop was not found.", exception));
        }
        catch (VoyageNotRevisableException exception)
        {
            return Conflict(ToConflict(
                "The voyage cannot be revised from its current status.",
                exception));
        }
        catch (DelayImpactAlreadyAppliedException exception)
        {
            return Conflict(ToConflict("The delay impact was already applied.", exception));
        }
        catch (StaleDelayImpactPreviewException exception)
        {
            return Conflict(ToConflict(
                "The impact preview is no longer current.",
                exception));
        }
        catch (VoyageStopNotRevisableException exception)
        {
            return Conflict(ToConflict(
                "An executed voyage stop cannot be revised.",
                exception));
        }
        catch (VoyageEstimateChronologyException exception)
        {
            return Conflict(ToConflict(
                "The revised estimates do not preserve the voyage chronology.",
                exception));
        }
    }

    [HttpGet("{voyageId:guid}/estimate-revisions")]
    [ProducesResponseType<VoyageEstimateRevisionsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VoyageEstimateRevisionsResult>> GetEstimateRevisions(
        Guid voyageId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetAsync(voyageId, cancellationToken));
        }
        catch (VoyageNotFoundException exception)
        {
            return NotFound(ToProblem("The requested voyage was not found.", exception));
        }
    }

    [HttpPost("{voyageId:guid}/estimate-revisions/{revisionBatchId:guid}/alert")]
    [ProducesResponseType<EstimateRevisionAlertResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EstimateRevisionAlertResult>> RetryAlert(
        Guid voyageId,
        Guid revisionBatchId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.RetryAlertAsync(
                voyageId,
                revisionBatchId,
                cancellationToken);

            return result is null
                ? NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "The requested estimate revision was not found.",
                    Detail = $"Estimate revision '{revisionBatchId}' was not found.",
                })
                : Ok(result);
        }
        catch (VoyageNotFoundException exception)
        {
            return NotFound(ToProblem("The requested voyage was not found.", exception));
        }
    }

    private Guid GetActingUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static ProblemDetails ToProblem(string title, Exception exception)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = title,
            Detail = exception.Message,
        };
    }

    private static ProblemDetails ToConflict(string title, Exception exception)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = title,
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
