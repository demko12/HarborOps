using System.Security.Claims;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Fleet.Presentation;

[ApiController]
[Route("api/fleet/vessels")]
public sealed class VesselsController(
    RegisterVesselService registerVesselService,
    ISearchVesselsQuery searchVesselsQuery,
    IVesselLookupService vesselLookupService,
    IGetVesselDetailsQuery getVesselDetailsQuery,
    RecordVesselStatusChangeService recordVesselStatusChangeService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SearchVesselsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchVesselsResponse>> Search(
        [FromQuery] SearchVesselsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await searchVesselsQuery.SearchAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (SearchVesselsValidationException exception)
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

    [HttpPost]
    [ProducesResponseType<RegisterVesselResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterVesselResponse>> Register(
        [FromBody] RegisterVesselRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerVesselService.RegisterAsync(request, cancellationToken);

            return Created($"/api/fleet/vessels/{response.Id}", response);
        }
        catch (RegisterVesselValidationException exception)
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
        catch (VesselTypeNotFoundException exception)
        {
            return NotFound(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterVesselValidator.VesselTypeIdField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested vessel type was not found.",
            });
        }
        catch (DuplicateImoNumberException exception)
        {
            return Conflict(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterVesselValidator.ImoNumberField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A vessel with this IMO number already exists.",
            });
        }
    }

    [HttpPost("lookup")]
    [ProducesResponseType<VesselLookupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VesselLookupResponse>> Lookup(
        [FromBody] VesselLookupRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await vesselLookupService.LookupAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (VesselLookupValidationException exception)
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

    [HttpGet("{vesselId:guid}")]
    [ProducesResponseType<VesselDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VesselDetailsResponse>> GetDetails(
        Guid vesselId,
        CancellationToken cancellationToken)
    {
        var response = await getVesselDetailsQuery.GetAsync(vesselId, cancellationToken);

        return response is null ? NotFound() : Ok(response);
    }

    [HttpPost("{vesselId:guid}/status-changes")]
    [ProducesResponseType<RecordVesselStatusChangeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordVesselStatusChangeResponse>> RecordStatusChange(
    Guid vesselId,
    [FromBody] RecordVesselStatusChangeRequest request,
    CancellationToken cancellationToken)
    {
        var changedBy = User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(changedBy))
        {
            return Unauthorized();
        }

        try
        {
            var response = await recordVesselStatusChangeService.RecordAsync(
                vesselId,
                changedBy,
                request,
                cancellationToken);

            return Created(
                $"/api/fleet/vessels/{vesselId}/status-changes/{response.Id}",
                response);
        }
        catch (RecordVesselStatusChangeValidationException exception)
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
        catch (VesselNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Vessel not found.",
                Detail = exception.Message,
            });
        }
        catch (VesselStatusUnchangedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Vessel status is unchanged.",
                Detail = exception.Message,
            });
        }
    }

}
