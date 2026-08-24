using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Voyages.Presentation;

[ApiController]
[Route("api/voyages/ports")]
public sealed class PortsController(
    SearchPortsQuery searchPortsQuery,
    PortLookupService portLookupService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SearchPortsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchPortsResponse>> Search(
        [FromQuery] SearchPortsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await searchPortsQuery.SearchAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (SearchPortsValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
    }

    [HttpPost("lookup")]
    [Authorize]
    [ProducesResponseType<PortLookupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PortLookupResponse>> Lookup(
        [FromBody] PortLookupRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await portLookupService.LookupAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (PortLookupValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
    }

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
