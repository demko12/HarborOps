using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/positions")]
public sealed class CrewPositionsController(ISearchCrewPositionsQuery searchCrewPositionsQuery)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SearchCrewPositionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchCrewPositionsResponse>> Search(
        [FromQuery] SearchCrewPositionsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await searchCrewPositionsQuery.SearchAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (SearchCrewPositionsValidationException exception)
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
}
