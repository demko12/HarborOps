using HarborOps.Modules.Fleet.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Fleet.Presentation;

[ApiController]
[Route("api/fleet/vessel-types")]
public sealed class VesselTypesController(IGetVesselTypesQuery getVesselTypesQuery)
    : ControllerBase
{
    private const string ReadFleetPolicy = "fleet.read";

    [HttpGet]
    [Authorize(ReadFleetPolicy)]
    [ProducesResponseType<VesselTypesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<VesselTypesResponse>> GetVesselTypes(
        CancellationToken cancellationToken)
    {
        var response = await getVesselTypesQuery.GetAsync(cancellationToken);

        return Ok(response);
    }
}
