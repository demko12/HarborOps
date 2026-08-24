using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Fleet.Presentation;

[ApiController]
[Route("api/fleet/vessels/{vesselId:guid}/fleet-assignments")]
public sealed class FleetAssignmentsController(IFleetManagementQuery query, FleetManagementService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FleetAssignmentHistoryResponse>> History(Guid vesselId, CancellationToken cancellationToken)
    {
        var response = await query.GetAssignmentHistoryAsync(vesselId, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<AssignFleetResponse>> Assign(Guid vesselId, [FromBody] AssignFleetRequest request, CancellationToken cancellationToken)
    {
        try { return Created(string.Empty, await service.AssignAsync(vesselId, request.FleetId, cancellationToken)); }
        catch (FleetAssignmentVesselNotFoundException) { return NotFound(new ProblemDetails { Title = "Vessel not found." }); }
        catch (FleetNotFoundException) { return NotFound(new ProblemDetails { Title = "Fleet not found." }); }
        catch (VesselAlreadyAssignedToFleetException) { return Conflict(new ProblemDetails { Title = "The vessel is already assigned to this fleet." }); }
    }

    [HttpPost("end")]
    public async Task<IActionResult> End(Guid vesselId, CancellationToken cancellationToken)
    {
        try { await service.EndAsync(vesselId, cancellationToken); return NoContent(); }
        catch (FleetAssignmentVesselNotFoundException) { return NotFound(new ProblemDetails { Title = "Vessel not found." }); }
        catch (NoActiveFleetAssignmentException) { return Conflict(new ProblemDetails { Title = "The vessel has no active fleet assignment." }); }
    }
}
