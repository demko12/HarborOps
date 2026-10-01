using System.Security.Claims;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Compliance.Presentation;

[ApiController]
[Route("api/compliance/non-conformities")]
public sealed class NonConformitiesController(
    RegisterNonConformityService registerNonConformityService,
    IGetNonConformitiesQuery getNonConformitiesQuery,
    IGetNonConformityDetailQuery getNonConformityDetailQuery,
    RecordCorrectiveActionService recordCorrectiveActionService,
    CloseNonConformityService closeNonConformityService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterNonConformityResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegisterNonConformityResponse>> Register(
        [FromBody] RegisterNonConformityRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerNonConformityService.RegisterAsync(request, cancellationToken);

            return Created($"/api/compliance/non-conformities/{response.Id}", response);
        }
        catch (RegisterNonConformityValidationException exception)
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
        catch (SurveyAuditNotFoundException exception)
        {
            return NotFound(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterNonConformityValidator.SurveyIdField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested survey audit was not found.",
            });
        }
        catch (PscInspectionNotFoundException exception)
        {
            return NotFound(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterNonConformityValidator.PscIdField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested PSC inspection was not found.",
            });
        }
    }

    [HttpGet]
    [ProducesResponseType<GetNonConformitiesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetNonConformitiesResponse>> Get(
        [FromQuery] GetNonConformitiesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getNonConformitiesQuery.GetAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetNonConformitiesValidationException exception)
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
    }

    [HttpGet("summary")]
    [ProducesResponseType<GetNonConformitiesSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GetNonConformitiesSummaryResponse>> GetSummary(CancellationToken cancellationToken) =>
        Ok(await getNonConformitiesQuery.GetSummaryAsync(cancellationToken));

    [HttpGet("{nonConformityId:int}")]
    [ProducesResponseType<GetNonConformityDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetNonConformityDetailResponse>> GetDetail(
        int nonConformityId,
        CancellationToken cancellationToken)
    {
        var response = await getNonConformityDetailQuery.GetAsync(nonConformityId, cancellationToken);

        return response is null ? NotFound() : Ok(response);
    }

    [HttpPost("{nonConformityId:int}/corrective-actions")]
    [ProducesResponseType<RecordCorrectiveActionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordCorrectiveActionResponse>> RecordCorrectiveAction(
        int nonConformityId,
        [FromBody] RecordCorrectiveActionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await recordCorrectiveActionService.RecordAsync(
                nonConformityId,
                GetActingUserId(),
                request,
                cancellationToken);

            return Created($"/api/compliance/non-conformities/{nonConformityId}", response);
        }
        catch (RecordCorrectiveActionValidationException exception)
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
        catch (NonConformityNotFoundException exception)
        {
            return NonConformityNotFound(exception);
        }
        catch (InvalidNonConformityTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
    }

    [HttpPost("{nonConformityId:int}/close")]
    [ProducesResponseType<CloseNonConformityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CloseNonConformityResponse>> Close(
        int nonConformityId,
        [FromBody] CloseNonConformityRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await closeNonConformityService.CloseAsync(
                nonConformityId,
                GetActingUserId(),
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (CloseNonConformityValidationException exception)
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
        catch (NonConformityNotFoundException exception)
        {
            return NonConformityNotFound(exception);
        }
        catch (InvalidNonConformityTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
    }

    private Guid GetActingUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private ActionResult NonConformityNotFound(NonConformityNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Non-conformity not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult TransitionNotAllowed(InvalidNonConformityTransitionException exception)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Non-conformity transition is not allowed.",
            Detail = exception.Message,
        });
    }
}
