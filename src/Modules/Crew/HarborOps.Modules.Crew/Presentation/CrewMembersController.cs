using System.Security.Claims;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/members")]
public sealed class CrewMembersController(
    RegisterCrewMemberService registerCrewMemberService,
    ISearchCrewMembersQuery searchCrewMembersQuery,
    IGetCrewMemberProfileQuery getCrewMemberProfileQuery,
    UpdateCrewMemberService updateCrewMemberService,
    ChangeCrewMemberEmploymentStatusService changeCrewMemberEmploymentStatusService,
    IGetCrewEmploymentStatusHistoryQuery getCrewEmploymentStatusHistoryQuery)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SearchCrewMembersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchCrewMembersResponse>> Search(
        [FromQuery] SearchCrewMembersRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await searchCrewMembersQuery.SearchAsync(request, cancellationToken));
        }
        catch (SearchCrewMembersValidationException exception)
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

    [HttpGet("summary")]
    [ProducesResponseType<CrewMembersSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CrewMembersSummaryResponse>> GetSummary(CancellationToken cancellationToken) =>
        Ok(await searchCrewMembersQuery.GetSummaryAsync(cancellationToken));

    [HttpPost]
    [ProducesResponseType<RegisterCrewMemberResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterCrewMemberResponse>> Register(
        [FromBody] RegisterCrewMemberRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerCrewMemberService.RegisterAsync(request, cancellationToken);

            return Created($"/api/crew/members/{response.Id}", response);
        }
        catch (RegisterCrewMemberValidationException exception)
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
        catch (CrewPositionNotFoundException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterCrewMemberValidator.BaseRankIdField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }
        catch (DuplicateImoSeafarerIdException exception)
        {
            return Conflict(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterCrewMemberValidator.ImoSeafarerIdField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A crew member with this IMO seafarer ID already exists.",
            });
        }
    }

    [HttpGet("{crewMemberId:guid}/profile")]
    [ProducesResponseType<CrewMemberProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CrewMemberProfileResponse>> GetProfile(
        Guid crewMemberId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getCrewMemberProfileQuery.GetAsync(crewMemberId, cancellationToken);
            return Ok(response);
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
    }

    [HttpPut("{crewMemberId:guid}")]
    [ProducesResponseType<UpdateCrewMemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UpdateCrewMemberResponse>> Update(
        Guid crewMemberId,
        [FromBody] UpdateCrewMemberRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await updateCrewMemberService.UpdateAsync(crewMemberId, request, cancellationToken));
        }
        catch (UpdateCrewMemberValidationException exception)
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
                Title = "Base rank not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{crewMemberId:guid}/employment-status-changes")]
    [ProducesResponseType<ChangeCrewMemberEmploymentStatusResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ChangeCrewMemberEmploymentStatusResponse>> ChangeEmploymentStatus(
        Guid crewMemberId,
        [FromBody] ChangeCrewMemberEmploymentStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await changeCrewMemberEmploymentStatusService.ChangeAsync(
                crewMemberId,
                GetActingUserId(),
                request,
                cancellationToken);

            return Created($"/api/crew/members/{crewMemberId}/employment-status-history", response);
        }
        catch (ChangeCrewMemberEmploymentStatusValidationException exception)
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
        catch (CrewMemberNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Crew member not found.",
                Detail = exception.Message,
            });
        }
        catch (CrewMemberEmploymentStatusUnchangedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Employment status is unchanged.",
                Detail = exception.Message,
            });
        }
        catch (CrewMemberHasOpenAssignmentOrRotationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Crew member cannot be terminated.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet("{crewMemberId:guid}/employment-status-history")]
    [ProducesResponseType<GetCrewEmploymentStatusHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetCrewEmploymentStatusHistoryResponse>> GetEmploymentStatusHistory(
        Guid crewMemberId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getCrewEmploymentStatusHistoryQuery.GetAsync(crewMemberId, cancellationToken));
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
    }

    private Guid GetActingUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
