using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Voyages.Presentation;

[ApiController]
[Route("api/voyages/ports")]
public sealed class PortsController(
    RegisterPortService registerPortService,
    ISearchPortsQuery searchPortsQuery,
    IPortLookupQuery portLookupQuery)
    : ControllerBase
{
    private const string ManageVoyagesPolicy = "voyages.manage";

    private const string ReadVoyagesPolicy = "voyages.read";

    [HttpPost]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<RegisterPortResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterPortResponse>> Register(
        [FromBody] RegisterPortRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerPortService.RegisterAsync(request, cancellationToken);

            return Created($"/api/voyages/ports/{response.Id}", response);
        }
        catch (RegisterPortValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (DuplicateUnLocodeException exception)
        {
            return Conflict(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterPortValidator.UnLocodeField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A port with this UN/LOCODE already exists.",
            });
        }
    }

    [HttpGet]
    [Authorize(ReadVoyagesPolicy)]
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

    [HttpGet("lookup")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<PortLookupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PortLookupResponse>> Lookup(
        [FromQuery] PortLookupRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await portLookupQuery.LookupAsync(request, cancellationToken);

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
