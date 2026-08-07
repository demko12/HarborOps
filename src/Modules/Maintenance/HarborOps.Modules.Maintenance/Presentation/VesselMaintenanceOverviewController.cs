using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/vessel-overviews")]
public sealed class VesselMaintenanceOverviewController(
    GetVesselMaintenanceOverviewService getVesselMaintenanceOverviewService)
    : ControllerBase
{
    [HttpPost("query")]
    [ProducesResponseType<VesselMaintenanceOverviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VesselMaintenanceOverviewRequest>> GetVesselMaintenanceOverview(
        [FromBody] VesselMaintenanceOverviewRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getVesselMaintenanceOverviewService.GetVesselMaintenanceOverviewAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (GetVesselMaintenanceOverviewValidationException exception)
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
