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
    IGetCrewMemberProfileQuery getCrewMemberProfileQuery)
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

    // Uses an absolute route (rather than a relative "{crewMemberId:guid}/profile" combined with
    // the controller's [Route("api/crew/members")]) because CapabilityAuthorizationConvention reads
    // the action's own AttributeRouteModel.Template before controller/action route combination runs.
    // A relative template here would be evaluated against BusinessCapabilityRouting on its own
    // (e.g. "{crewMemberId:guid}/profile"), never matching "/api/crew", and silently drop the
    // crew.read policy from this endpoint.
    [HttpGet("/api/crew/members/{crewMemberId:guid}/profile")]
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
}
