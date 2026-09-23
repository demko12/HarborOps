using System.Security.Claims;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Voyages.Presentation;

[ApiController]
[Route("api/voyages/{voyageId:guid}/stops/{stopId:guid}")]
public sealed class PortCallLogController(
    IGetPortCallActivitiesQuery getPortCallActivitiesQuery,
    RecordPortCallActivityService recordPortCallActivityService,
    CorrectPortCallActivityService correctPortCallActivityService,
    VoidPortCallActivityService voidPortCallActivityService,
    IGetPortCallStatementQuery getPortCallStatementQuery,
    CreatePortCallStatementService createPortCallStatementService,
    FinalizePortCallStatementService finalizePortCallStatementService)
    : ControllerBase
{
    private const string ManageVoyagesPolicy = "voyages.manage";

    private const string ReadVoyagesPolicy = "voyages.read";

    [HttpGet("activity-timeline")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<PortCallTimelineResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortCallTimelineResponse>> GetTimeline(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getPortCallActivitiesQuery.GetTimelineAsync(
                voyageId,
                stopId,
                cancellationToken));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
    }

    [HttpGet("activities")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<PortCallActivitiesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortCallActivitiesResponse>> GetActivities(
        Guid voyageId,
        Guid stopId,
        [FromQuery] GetPortCallActivitiesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getPortCallActivitiesQuery.GetActivitiesAsync(
                voyageId,
                stopId,
                request,
                cancellationToken));
        }
        catch (PortCallLogValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
    }

    [HttpGet("activities/{activityId:guid}")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<PortCallActivityDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortCallActivityDetailsResponse>> GetActivity(
        Guid voyageId,
        Guid stopId,
        Guid activityId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getPortCallActivitiesQuery.GetActivityAsync(
                voyageId,
                stopId,
                activityId,
                cancellationToken));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallActivityNotFoundException exception)
        {
            return ActivityNotFound(exception);
        }
    }

    [HttpPost("activities")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<PortCallActivityChangeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortCallActivityChangeResponse>> RecordActivity(
        Guid voyageId,
        Guid stopId,
        [FromBody] RecordPortCallActivityRequest request,
        CancellationToken cancellationToken)
    {
        var actorName = GetActingUserName();
        if (actorName is null)
        {
            return Unauthorized();
        }

        try
        {
            var response = await recordPortCallActivityService.RecordAsync(
                voyageId,
                stopId,
                request,
                GetActingUserId(),
                actorName,
                cancellationToken);

            return Created(
                $"/api/voyages/{voyageId}/stops/{stopId}/activities/{response.ActivityId}",
                response);
        }
        catch (PortCallLogValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallActivityStopNotArrivedException exception)
        {
            return ConflictProblem("The voyage stop has not arrived yet.", exception.Message);
        }
        catch (PortCallActivityLimitReachedException exception)
        {
            return ConflictProblem("The activity log of this stop is full.", exception.Message);
        }
        catch (PortCallStatementAlreadyFinalizedException exception)
        {
            return StatementFinalized(exception);
        }
        catch (PortCallLogConflictException)
        {
            return ConcurrentChange();
        }
    }

    [HttpPost("activities/{activityId:guid}/correct")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<PortCallActivityChangeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortCallActivityChangeResponse>> CorrectActivity(
        Guid voyageId,
        Guid stopId,
        Guid activityId,
        [FromBody] CorrectPortCallActivityRequest request,
        CancellationToken cancellationToken)
    {
        var actorName = GetActingUserName();
        if (actorName is null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await correctPortCallActivityService.CorrectAsync(
                voyageId,
                stopId,
                activityId,
                request,
                GetActingUserId(),
                actorName,
                cancellationToken));
        }
        catch (PortCallLogValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallActivityNotFoundException exception)
        {
            return ActivityNotFound(exception);
        }
        catch (PortCallActivityVoidedException exception)
        {
            return ActivityVoided(exception);
        }
        catch (PortCallActivityLockedException exception)
        {
            return ActivityLocked(exception);
        }
        catch (PortCallStatementAlreadyFinalizedException exception)
        {
            return StatementFinalized(exception);
        }
        catch (PortCallLogConflictException)
        {
            return ConcurrentChange();
        }
    }

    [HttpPost("activities/{activityId:guid}/void")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<PortCallActivityChangeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortCallActivityChangeResponse>> VoidActivity(
        Guid voyageId,
        Guid stopId,
        Guid activityId,
        [FromBody] VoidPortCallActivityRequest request,
        CancellationToken cancellationToken)
    {
        var actorName = GetActingUserName();
        if (actorName is null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await voidPortCallActivityService.VoidAsync(
                voyageId,
                stopId,
                activityId,
                request,
                GetActingUserId(),
                actorName,
                cancellationToken));
        }
        catch (PortCallLogValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallActivityNotFoundException exception)
        {
            return ActivityNotFound(exception);
        }
        catch (PortCallActivityVoidedException exception)
        {
            return ActivityVoided(exception);
        }
        catch (PortCallActivityLockedException exception)
        {
            return ActivityLocked(exception);
        }
        catch (PortCallStatementAlreadyFinalizedException exception)
        {
            return StatementFinalized(exception);
        }
        catch (PortCallLogConflictException)
        {
            return ConcurrentChange();
        }
    }

    [HttpGet("statement")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<PortCallStatementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortCallStatementResponse>> GetStatement(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getPortCallStatementQuery.GetAsync(voyageId, stopId, cancellationToken));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallStatementNotFoundException exception)
        {
            return StatementNotFound(exception);
        }
    }

    [HttpPost("statement")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<PortCallStatementResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortCallStatementResponse>> CreateStatement(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        try
        {
            await createPortCallStatementService.CreateAsync(
                voyageId,
                stopId,
                GetActingUserId(),
                cancellationToken);

            return Created(
                $"/api/voyages/{voyageId}/stops/{stopId}/statement",
                await getPortCallStatementQuery.GetAsync(voyageId, stopId, cancellationToken));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallStatementAlreadyExistsException exception)
        {
            return ConflictProblem("The voyage stop already has a statement.", exception.Message);
        }
        catch (PortCallLogConflictException)
        {
            return ConcurrentChange();
        }
    }

    [HttpPost("statement/finalize")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<PortCallStatementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortCallStatementResponse>> FinalizeStatement(
        Guid voyageId,
        Guid stopId,
        [FromBody] FinalizePortCallStatementRequest request,
        CancellationToken cancellationToken)
    {
        var actorName = GetActingUserName();
        if (actorName is null)
        {
            return Unauthorized();
        }

        try
        {
            await finalizePortCallStatementService.FinalizeAsync(
                voyageId,
                stopId,
                request,
                GetActingUserId(),
                actorName,
                cancellationToken);

            return Ok(await getPortCallStatementQuery.GetAsync(voyageId, stopId, cancellationToken));
        }
        catch (PortCallLogValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallStatementNotFoundException exception)
        {
            return StatementNotFound(exception);
        }
        catch (PortCallStatementAlreadyFinalizedException exception)
        {
            return StatementFinalized(exception);
        }
        catch (PortCallStatementNotFinalizableException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The statement cannot be finalized yet.",
                Detail = exception.Message,
                Extensions =
                {
                    ["blockers"] = exception.Blockers.Select(PortCallLog.ToItem).ToList(),
                },
            });
        }
        catch (PortCallStatementSourceChangedException exception)
        {
            return ConflictProblem(
                "The statement sources changed after the preview.",
                exception.Message + " Review the refreshed preview and confirm again.");
        }
        catch (PortCallLogConflictException)
        {
            return ConcurrentChange();
        }
    }

    [HttpGet("statement/print")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<PortCallStatementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortCallStatementResponse>> GetStatementPrint(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getPortCallStatementQuery.GetPrintAsync(voyageId, stopId, cancellationToken));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallStatementNotFoundException exception)
        {
            return StatementNotFound(exception);
        }
        catch (PortCallStatementNotFinalizedException exception)
        {
            return ConflictProblem("Only a finalized statement can be printed.", exception.Message);
        }
    }

    private Guid GetActingUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string? GetActingUserName()
    {
        var name = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.Name);

        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private NotFoundObjectResult VoyageNotFound(VoyageNotFoundException exception) =>
        NotFoundProblem("The requested voyage was not found.", exception.Message);

    private NotFoundObjectResult VoyageStopNotFound(VoyageStopNotFoundException exception) =>
        NotFoundProblem("The requested voyage stop was not found.", exception.Message);

    private NotFoundObjectResult ActivityNotFound(PortCallActivityNotFoundException exception) =>
        NotFoundProblem("The requested port-call activity was not found.", exception.Message);

    private NotFoundObjectResult StatementNotFound(PortCallStatementNotFoundException exception) =>
        NotFoundProblem("The requested port-call statement was not found.", exception.Message);

    private ConflictObjectResult ActivityVoided(PortCallActivityVoidedException exception) =>
        ConflictProblem("The port-call activity is voided.", exception.Message);

    private ConflictObjectResult ActivityLocked(PortCallActivityLockedException exception) =>
        ConflictProblem("The port-call activity belongs to a finalized statement.", exception.Message);

    private ConflictObjectResult StatementFinalized(PortCallStatementAlreadyFinalizedException exception) =>
        ConflictProblem("The port-call statement is already finalized.", exception.Message);

    private ConflictObjectResult ConcurrentChange() =>
        ConflictProblem(
            "The activity log was modified concurrently.",
            "The activity log or statement was changed by another request. Reload and try again.");

    private NotFoundObjectResult NotFoundProblem(string title, string detail) =>
        NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = title,
            Detail = detail,
        });

    private ConflictObjectResult ConflictProblem(string title, string detail) =>
        Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = title,
            Detail = detail,
        });

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
