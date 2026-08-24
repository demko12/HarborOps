using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Fleet.Presentation;

[ApiController]
[Route("api/fleet/fleets")]
public sealed class FleetsController(IFleetManagementQuery query, FleetManagementService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SearchFleetsResponse>> Search([FromQuery] SearchFleetsRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await query.SearchAsync(request, cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Title = "Invalid fleet search", Detail = exception.Message }); }
    }

    [HttpPost]
    public async Task<ActionResult<CreateFleetResponse>> Create([FromBody] CreateFleetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await service.CreateAsync(request, cancellationToken);
            return Created($"/api/fleet/fleets/{response.FleetId}", response);
        }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Title = "Invalid fleet", Detail = exception.Message }); }
        catch (DuplicateFleetNameException) { return Conflict(new ProblemDetails { Title = "A fleet with this name already exists." }); }
    }

    [HttpGet("{fleetId:guid}")]
    public async Task<ActionResult<FleetDetailsResponse>> Get(Guid fleetId, CancellationToken cancellationToken)
    {
        var response = await query.GetDetailsAsync(fleetId, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }
}
