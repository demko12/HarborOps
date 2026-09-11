using System.Security.Claims;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/availability")]
public sealed class CrewAvailabilityController(
    IGetCrewAvailabilityQuery getCrewAvailabilityQuery,
    IGetCrewMemberAvailabilityQuery getCrewMemberAvailabilityQuery,
    IGetCrewAvailabilityConflictsQuery getCrewAvailabilityConflictsQuery,
    RequestCrewAvailabilityService requestCrewAvailabilityService,
    ApproveCrewAvailabilityRequestService approveCrewAvailabilityRequestService,
    RejectCrewAvailabilityRequestService rejectCrewAvailabilityRequestService,
    CancelCrewAvailabilityRequestService cancelCrewAvailabilityRequestService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetCrewAvailabilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetCrewAvailabilityResponse>> GetAvailability(
        [FromQuery] GetCrewAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getCrewAvailabilityQuery.GetAsync(request, cancellationToken));
        }
        catch (GetCrewAvailabilityValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
    }

    [HttpGet("conflicts")]
    [ProducesResponseType<GetCrewAvailabilityConflictsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetCrewAvailabilityConflictsResponse>> GetConflicts(
        [FromQuery] GetCrewAvailabilityConflictsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getCrewAvailabilityConflictsQuery.GetAsync(request, cancellationToken));
        }
        catch (GetCrewAvailabilityConflictsValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
    }

    [HttpGet("/api/crew/members/{crewMemberId:guid}/availability")]
    [ProducesResponseType<GetCrewMemberAvailabilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetCrewMemberAvailabilityResponse>> GetMemberAvailability(
        Guid crewMemberId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getCrewMemberAvailabilityQuery.GetAsync(crewMemberId, cancellationToken));
        }
        catch (CrewMemberNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew member not found.", exception.Message));
        }
    }

    [HttpPost("/api/crew/members/{crewMemberId:guid}/availability-requests")]
    [ProducesResponseType<CreateCrewAvailabilityRequestResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateCrewAvailabilityRequestResponse>> RequestAvailability(
        Guid crewMemberId,
        [FromBody] CreateCrewAvailabilityRequestRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await requestCrewAvailabilityService.RequestAsync(
                crewMemberId, GetActingUserId(), request, cancellationToken);

            return Created($"/api/crew/availability?crewMemberId={crewMemberId}", response);
        }
        catch (CreateCrewAvailabilityRequestValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (CrewMemberNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew member not found.", exception.Message));
        }
        catch (OverlappingCrewAvailabilityRequestException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "Crew member already has an overlapping unavailability period.",
                exception.Message));
        }
    }

    [HttpPost("/api/crew/availability-requests/{requestId:guid}/approve")]
    [ProducesResponseType<CrewAvailabilityDecisionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CrewAvailabilityDecisionResponse>> Approve(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await approveCrewAvailabilityRequestService.ApproveAsync(
                requestId, GetActingUserId(), cancellationToken));
        }
        catch (CrewAvailabilityRequestNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew availability request not found.", exception.Message));
        }
        catch (CrewAvailabilitySelfApprovalException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "A crew availability request cannot be approved by its requester.",
                exception.Message));
        }
        catch (CrewAvailabilityApprovalConflictException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "Crew availability request conflicts with a planned assignment or rotation.",
                exception.Message));
        }
        catch (InvalidCrewAvailabilityTransitionException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "Crew availability request cannot be approved.",
                exception.Message));
        }
    }

    [HttpPost("/api/crew/availability-requests/{requestId:guid}/reject")]
    [ProducesResponseType<CrewAvailabilityDecisionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CrewAvailabilityDecisionResponse>> Reject(
        Guid requestId,
        [FromBody] RejectCrewAvailabilityRequestRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await rejectCrewAvailabilityRequestService.RejectAsync(
                requestId, GetActingUserId(), request, cancellationToken));
        }
        catch (RejectCrewAvailabilityRequestValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (CrewAvailabilityRequestNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew availability request not found.", exception.Message));
        }
        catch (InvalidCrewAvailabilityTransitionException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "Crew availability request cannot be rejected.",
                exception.Message));
        }
    }

    [HttpPost("/api/crew/availability-requests/{requestId:guid}/cancel")]
    [ProducesResponseType<CrewAvailabilityDecisionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CrewAvailabilityDecisionResponse>> Cancel(
        Guid requestId,
        [FromBody] CancelCrewAvailabilityRequestRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await cancelCrewAvailabilityRequestService.CancelAsync(
                requestId, GetActingUserId(), request, cancellationToken));
        }
        catch (CancelCrewAvailabilityRequestValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (CrewAvailabilityRequestNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew availability request not found.", exception.Message));
        }
        catch (InvalidCrewAvailabilityTransitionException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "Crew availability request cannot be cancelled.",
                exception.Message));
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
