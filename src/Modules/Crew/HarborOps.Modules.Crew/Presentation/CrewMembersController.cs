using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/members")]
public sealed class CrewMembersController(RegisterCrewMemberService registerCrewMemberService)
    : ControllerBase
{
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
}
