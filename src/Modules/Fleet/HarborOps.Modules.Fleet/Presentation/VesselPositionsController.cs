using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Fleet.Presentation;


[ApiController]
[Route("api/fleet/vessel-positions")]
public sealed class VesselPositionsController(IVesselPositionsQuery vesselPositionsQuery)
    : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<VesselPositionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<VesselPositionsResponse>> Get(
        [FromQuery] VesselPositionsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await vesselPositionsQuery.GetAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (VesselPositionsValidationException exception)
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
