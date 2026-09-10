using System.Security.Claims;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Compliance.Presentation;

[ApiController]
[Route("api/compliance/audit-plans")]
public sealed class AuditPlansController(
    IGetAuditPlansQuery getAuditPlansQuery,
    IGetAuditPlanDetailQuery getAuditPlanDetailQuery,
    RegisterAuditPlanService registerAuditPlanService,
    RescheduleAuditPlanService rescheduleAuditPlanService,
    AssignAuditPlanInspectorService assignAuditPlanInspectorService,
    StartAuditPlanService startAuditPlanService,
    CompleteAuditPlanService completeAuditPlanService,
    CancelAuditPlanService cancelAuditPlanService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetAuditPlansResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetAuditPlansResponse>> Get(
        [FromQuery] GetAuditPlansRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getAuditPlansQuery.GetAsync(request, cancellationToken));
        }
        catch (GetAuditPlansValidationException exception)
        {
            return ValidationProblem(exception.Errors);
        }
    }

    [HttpGet("{auditPlanId:guid}")]
    [ProducesResponseType<GetAuditPlanDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetAuditPlanDetailResponse>> GetDetail(
        Guid auditPlanId,
        CancellationToken cancellationToken)
    {
        var response = await getAuditPlanDetailQuery.GetAsync(auditPlanId, cancellationToken);

        return response is null ? NotFound() : Ok(response);
    }

    [HttpPost]
    [ProducesResponseType<RegisterAuditPlanResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterAuditPlanResponse>> Register(
        [FromBody] RegisterAuditPlanRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerAuditPlanService.RegisterAsync(request, cancellationToken);

            return Created($"/api/compliance/audit-plans/{response.Id}", response);
        }
        catch (RegisterAuditPlanValidationException exception)
        {
            return ValidationProblem(exception.Errors);
        }
        catch (OverlappingAuditPlanException exception)
        {
            return Overlapping(exception);
        }
    }

    [HttpPut("{auditPlanId:guid}/schedule")]
    [ProducesResponseType<RescheduleAuditPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RescheduleAuditPlanResponse>> Reschedule(
        Guid auditPlanId,
        [FromBody] RescheduleAuditPlanRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await rescheduleAuditPlanService.RescheduleAsync(
                auditPlanId, GetActingUserId(), request, cancellationToken));
        }
        catch (RescheduleAuditPlanValidationException exception)
        {
            return ValidationProblem(exception.Errors);
        }
        catch (AuditPlanNotFoundException exception)
        {
            return AuditPlanNotFound(exception);
        }
        catch (InvalidAuditPlanTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
        catch (OverlappingAuditPlanException exception)
        {
            return Overlapping(exception);
        }
    }

    [HttpPut("{auditPlanId:guid}/assignment")]
    [ProducesResponseType<AssignAuditPlanInspectorResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssignAuditPlanInspectorResponse>> Assign(
        Guid auditPlanId,
        [FromBody] AssignAuditPlanInspectorRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await assignAuditPlanInspectorService.AssignAsync(
                auditPlanId, GetActingUserId(), request, cancellationToken));
        }
        catch (AssignAuditPlanInspectorValidationException exception)
        {
            return ValidationProblem(exception.Errors);
        }
        catch (AuditPlanNotFoundException exception)
        {
            return AuditPlanNotFound(exception);
        }
        catch (InvalidAuditPlanTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
    }

    [HttpPost("{auditPlanId:guid}/start")]
    [ProducesResponseType<StartAuditPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StartAuditPlanResponse>> Start(
        Guid auditPlanId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await startAuditPlanService.StartAsync(auditPlanId, GetActingUserId(), cancellationToken));
        }
        catch (AuditPlanNotFoundException exception)
        {
            return AuditPlanNotFound(exception);
        }
        catch (InvalidAuditPlanTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
    }

    [HttpPost("{auditPlanId:guid}/complete")]
    [ProducesResponseType<CompleteAuditPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CompleteAuditPlanResponse>> Complete(
        Guid auditPlanId,
        [FromBody] CompleteAuditPlanRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await completeAuditPlanService.CompleteAsync(
                auditPlanId, GetActingUserId(), request, cancellationToken));
        }
        catch (CompleteAuditPlanValidationException exception)
        {
            return ValidationProblem(exception.Errors);
        }
        catch (AuditPlanNotFoundException exception)
        {
            return AuditPlanNotFound(exception);
        }
        catch (InvalidAuditPlanTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
    }

    [HttpPost("{auditPlanId:guid}/cancel")]
    [ProducesResponseType<CancelAuditPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CancelAuditPlanResponse>> Cancel(
        Guid auditPlanId,
        [FromBody] CancelAuditPlanRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await cancelAuditPlanService.CancelAsync(
                auditPlanId, GetActingUserId(), request, cancellationToken));
        }
        catch (CancelAuditPlanValidationException exception)
        {
            return ValidationProblem(exception.Errors);
        }
        catch (AuditPlanNotFoundException exception)
        {
            return AuditPlanNotFound(exception);
        }
        catch (InvalidAuditPlanTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
    }

    private Guid GetActingUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        return BadRequest(new HttpValidationProblemDetails(
            errors.ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        });
    }

    private ActionResult AuditPlanNotFound(AuditPlanNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Audit plan not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult TransitionNotAllowed(InvalidAuditPlanTransitionException exception)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Audit plan transition is not allowed.",
            Detail = exception.Message,
        });
    }

    private ActionResult Overlapping(OverlappingAuditPlanException exception)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Audit plan dates overlap an existing active plan.",
            Detail = exception.Message,
        });
    }
}
