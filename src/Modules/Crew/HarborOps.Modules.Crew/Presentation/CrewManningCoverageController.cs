using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/manning-coverage")]
public sealed class CrewManningCoverageController(IGetCrewManningCoverageQuery getCrewManningCoverageQuery)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetCrewManningCoverageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetCrewManningCoverageResponse>> Get(
        [FromQuery] GetCrewManningCoverageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getCrewManningCoverageQuery.GetAsync(request, cancellationToken));
        }
        catch (GetCrewManningCoverageValidationException exception)
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
