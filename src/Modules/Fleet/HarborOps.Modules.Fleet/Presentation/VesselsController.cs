using System.Security.Claims;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using Microsoft.AspNetCore.Authorization;
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
    RecordVesselStatusChangeService recordVesselStatusChangeService,
    RecordVesselPositionService recordVesselPositionService,
    UpdateVesselRegistryDetailsService updateVesselRegistryDetailsService,
    UpdateVesselSpecificationsService updateVesselSpecificationsService)
    : ControllerBase
{
    // The capability convention reads a single route template and prefers the action's, so an
    // action that carries its own relative segment never matches the "/api/fleet" prefix and is
    // left without a capability policy. These routes state their policy explicitly.
    private const string ReadFleetPolicy = "fleet.read";

    private const string ManageFleetPolicy = "fleet.manage";

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser superseded a type-ahead request. There is no response to send.
            return new EmptyResult();
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
    [Authorize(ReadFleetPolicy)]
    [ProducesResponseType<VesselDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VesselDetailsResponse>> GetDetails(
        Guid vesselId,
        CancellationToken cancellationToken)
    {
        var response = await getVesselDetailsQuery.GetAsync(vesselId, cancellationToken);

        return response is null ? NotFound() : Ok(response);
    }

    [HttpPut("{vesselId:guid}/registry-details")]
    [Authorize(ManageFleetPolicy)]
    [ProducesResponseType<VesselDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VesselDetailsResponse>> UpdateRegistryDetails(
        Guid vesselId,
        [FromBody] UpdateVesselRegistryDetailsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await updateVesselRegistryDetailsService.UpdateAsync(
                vesselId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (UpdateVesselRegistryDetailsValidationException exception)
        {
            return ValidationProblemResult(exception.Errors);
        }
        catch (VesselNotFoundException exception)
        {
            return NotFoundProblem(
                UpdateVesselRegistryDetailsValidator.NameField,
                exception.Message,
                "Vessel not found.");
        }
        catch (VesselTypeNotFoundException exception)
        {
            return NotFoundProblem(
                UpdateVesselRegistryDetailsValidator.VesselTypeIdField,
                exception.Message,
                "The requested vessel type was not found.");
        }
        catch (DuplicateMmsiException exception)
        {
            return ConflictProblem(
                UpdateVesselRegistryDetailsValidator.MmsiField,
                exception.Message,
                "A vessel with this MMSI already exists.");
        }
        catch (DuplicateCallSignException exception)
        {
            return ConflictProblem(
                UpdateVesselRegistryDetailsValidator.CallSignField,
                exception.Message,
                "A vessel with this call sign already exists.");
        }
    }

    [HttpPut("{vesselId:guid}/specifications")]
    [Authorize(ManageFleetPolicy)]
    [ProducesResponseType<VesselDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VesselDetailsResponse>> UpdateSpecifications(
        Guid vesselId,
        [FromBody] UpdateVesselSpecificationsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await updateVesselSpecificationsService.UpdateAsync(
                vesselId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (UpdateVesselSpecificationsValidationException exception)
        {
            return ValidationProblemResult(exception.Errors);
        }
        catch (VesselNotFoundException exception)
        {
            return NotFoundProblem(
                UpdateVesselSpecificationsValidator.DwtField,
                exception.Message,
                "Vessel not found.");
        }
    }

    [HttpPost("{vesselId:guid}/status-changes")]
    [Authorize(ManageFleetPolicy)]
    [ProducesResponseType<RecordVesselStatusChangeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

    [HttpPost("{vesselId:guid}/position-reports")]
    [Authorize(ManageFleetPolicy)]
    [ProducesResponseType<RecordVesselPositionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecordVesselPositionResponse>> RecordPosition(
    Guid vesselId,
    [FromBody] RecordVesselPositionRequest request,
    CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var reporterUserId)) return Unauthorized();
        try
        {
            var response = await recordVesselPositionService.RecordAsync(
                vesselId,
                request,
                reporterUserId,
                cancellationToken);

            return Ok(response);
        }
        catch (RecordVesselPositionValidationException exception)
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
    }

    [HttpGet("{vesselId:guid}/position-reports")]
    [Authorize(ReadFleetPolicy)]
    [ProducesResponseType<VesselPositionHistoryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<VesselPositionHistoryResponse>> GetPositionHistory(
        Guid vesselId, [FromQuery] VesselPositionHistoryRequest request, [FromServices] IVesselPositionHistoryQuery historyQuery, CancellationToken cancellationToken)
    {
        try { return Ok(await historyQuery.GetAsync(vesselId, request, cancellationToken)); }
        catch (VesselPositionHistoryValidationException) { return BadRequest(new ProblemDetails { Title = "Invalid position history date window." }); }
    }

    private ActionResult ValidationProblemResult(IReadOnlyDictionary<string, string[]> errors)
    {
        return BadRequest(new HttpValidationProblemDetails(
            errors.ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        });
    }

    private ActionResult NotFoundProblem(string field, string message, string title)
    {
        return NotFound(new HttpValidationProblemDetails(
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [message],
            })
        {
            Status = StatusCodes.Status404NotFound,
            Title = title,
            Detail = message,
        });
    }

    private ActionResult ConflictProblem(string field, string message, string title)
    {
        return Conflict(new HttpValidationProblemDetails(
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [message],
            })
        {
            Status = StatusCodes.Status409Conflict,
            Title = title,
            Detail = message,
        });
    }
}
