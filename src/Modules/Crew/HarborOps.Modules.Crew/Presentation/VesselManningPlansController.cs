using System.Security.Claims;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/manning-plans")]
public sealed class VesselManningPlansController(
    IGetVesselManningPlansQuery getVesselManningPlansQuery,
    IGetVesselManningPlanDetailQuery getVesselManningPlanDetailQuery,
    CreateVesselManningPlanService createVesselManningPlanService,
    ReplaceVesselManningPlanLinesService replaceVesselManningPlanLinesService,
    EndVesselManningPlanService endVesselManningPlanService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetVesselManningPlansResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetVesselManningPlansResponse>> List(
        [FromQuery] GetVesselManningPlansRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getVesselManningPlansQuery.GetAsync(request, cancellationToken));
        }
        catch (GetVesselManningPlansValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
    }

    [HttpGet("{planId:guid}")]
    [ProducesResponseType<GetVesselManningPlanDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetVesselManningPlanDetailResponse>> GetDetail(
        Guid planId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getVesselManningPlanDetailQuery.GetAsync(planId, cancellationToken));
        }
        catch (VesselManningPlanNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Vessel manning plan not found.", exception.Message));
        }
    }

    [HttpPost]
    [ProducesResponseType<CreateVesselManningPlanResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateVesselManningPlanResponse>> Create(
        [FromBody] CreateVesselManningPlanRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await createVesselManningPlanService.CreateAsync(GetActingUserId(), request, cancellationToken);

            return Created($"/api/crew/manning-plans/{response.Id}", response);
        }
        catch (CreateVesselManningPlanValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (VesselNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Vessel not found.", exception.Message));
        }
        catch (CrewPositionNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew position not found.", exception.Message));
        }
        catch (OverlappingVesselManningPlanException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "Vessel already has an overlapping manning plan.",
                exception.Message));
        }
    }

    [HttpPut("{planId:guid}/lines")]
    [ProducesResponseType<ReplaceVesselManningPlanLinesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReplaceVesselManningPlanLinesResponse>> ReplaceLines(
        Guid planId,
        [FromBody] ReplaceVesselManningPlanLinesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await replaceVesselManningPlanLinesService.ReplaceAsync(
                planId, GetActingUserId(), request, cancellationToken));
        }
        catch (ReplaceVesselManningPlanLinesValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (VesselManningPlanNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Vessel manning plan not found.", exception.Message));
        }
        catch (CrewPositionNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew position not found.", exception.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
    }

    [HttpPost("{planId:guid}/end-date")]
    [ProducesResponseType<EndVesselManningPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EndVesselManningPlanResponse>> EndDate(
        Guid planId,
        [FromBody] EndVesselManningPlanRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await endVesselManningPlanService.EndAsync(planId, GetActingUserId(), request, cancellationToken));
        }
        catch (EndVesselManningPlanValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (VesselManningPlanNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Vessel manning plan not found.", exception.Message));
        }
        catch (InvalidVesselManningPlanEndDateException exception)
        {
            return BadRequest(Problem(
                StatusCodes.Status400BadRequest, "Vessel manning plan end date is invalid.", exception.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
    }

    private Guid GetActingUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private ActionResult ConcurrentChange()
    {
        return Conflict(Problem(
            StatusCodes.Status409Conflict,
            "Vessel manning plan was modified concurrently.",
            "The manning plan was changed by another request. Reload and try again."));
    }

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
