using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Api.Composition.Operations.FleetVessel;

[ApiController]
[Authorize(Permissions.Fleet.Read)]
[Authorize(Permissions.Maintenance.Read)]
[Route("api/operations/fleets/{fleetId}/vessels")]
public sealed class FleetVesselOverviewController(
    FleetVesselOverviewService fleetVesselOverviewService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FleetVesselOverviewPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FleetVesselOverviewPage>> Get(
        Guid fleetId,
        CancellationToken cancellationToken,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null)
    {
        try
        {
            var page = await fleetVesselOverviewService.QueryAsync(
                fleetId,
                pageNumber,
                pageSize,
                search,
                cancellationToken);

            return Ok(page);
        }
        catch (FleetVesselOverviewValidationException exception)
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
