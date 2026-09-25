using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/qualification-eligibility")]
public sealed class CrewQualificationEligibilityController(IGetCrewQualificationEligibilityQuery getCrewQualificationEligibilityQuery)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetCrewQualificationEligibilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetCrewQualificationEligibilityResponse>> Get(
        [FromQuery] GetCrewQualificationEligibilityRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getCrewQualificationEligibilityQuery.GetAsync(request, cancellationToken));
        }
        catch (GetCrewQualificationEligibilityValidationException exception)
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
