using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Voyages.Presentation;

[ApiController]
[Route("api/voyages/routes")]
public sealed class RoutesController(
    RegisterRouteService registerRouteService,
    ISearchRoutesQuery searchRoutesQuery,
    IGetRouteDetailsQuery getRouteDetailsQuery,
    IRouteLookupQuery routeLookupQuery)
    : ControllerBase
{
    private const string ManageVoyagesPolicy = "voyages.manage";

    private const string ReadVoyagesPolicy = "voyages.read";

    [HttpPost]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<RegisterRouteResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegisterRouteResponse>> Register(
        [FromBody] RegisterRouteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerRouteService.RegisterAsync(request, cancellationToken);

            return Created($"/api/voyages/routes/{response.Id}", response);
        }
        catch (RegisterRouteValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (RouteStopPortNotFoundException exception)
        {
            return NotFound(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterRouteValidator.StopsField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested port was not found.",
            });
        }
    }

    [HttpGet]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<SearchRoutesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchRoutesResponse>> Search(
        [FromQuery] SearchRoutesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await searchRoutesQuery.SearchAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (SearchRoutesValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
    }

    [HttpGet("lookup")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<RouteLookupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RouteLookupResponse>> Lookup(
        [FromQuery] RouteLookupRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await routeLookupQuery.LookupAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (RouteLookupValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
    }

    [HttpGet("{routeId:guid}")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<RouteDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RouteDetailsResponse>> GetDetails(
        Guid routeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getRouteDetailsQuery.GetRouteDetailsAsync(
                routeId,
                cancellationToken);

            return Ok(response);
        }
        catch (RouteNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested route was not found.",
                Detail = exception.Message,
            });
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
