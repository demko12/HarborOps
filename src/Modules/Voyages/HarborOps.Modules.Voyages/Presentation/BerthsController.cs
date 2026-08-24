using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Voyages.Presentation;

[ApiController]
[Route("api/voyages/berths")]
public sealed class BerthsController(
    RegisterBerthService registerBerthService,
    ISearchBerthsQuery searchBerthsQuery,
    IBerthLookupQuery berthLookupQuery)
    : ControllerBase
{
    private const string ManageVoyagesPolicy = "voyages.manage";

    private const string ReadVoyagesPolicy = "voyages.read";

    [HttpPost]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<RegisterBerthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterBerthResponse>> Register(
        [FromBody] RegisterBerthRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerBerthService.RegisterAsync(request, cancellationToken);

            return Created($"/api/voyages/berths/{response.Id}", response);
        }
        catch (RegisterBerthValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (PortNotFoundException exception)
        {
            return NotFound(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterBerthValidator.PortIdField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested port was not found.",
            });
        }
        catch (DuplicateBerthNameException exception)
        {
            return Conflict(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterBerthValidator.NameField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A berth with this name already exists for the port.",
            });
        }
    }

    [HttpGet]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<SearchBerthsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchBerthsResponse>> Search(
        [FromQuery] SearchBerthsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await searchBerthsQuery.SearchAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (SearchBerthsValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
    }

    [HttpGet("lookup")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<BerthLookupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BerthLookupResponse>> Lookup(
        [FromQuery] BerthLookupRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await berthLookupQuery.LookupAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (BerthLookupValidationException exception)
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
