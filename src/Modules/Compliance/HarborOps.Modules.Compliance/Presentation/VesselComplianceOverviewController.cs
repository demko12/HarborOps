using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Compliance.Presentation;

[ApiController]
[Route("api/compliance/vessels/{vesselId}/overview")]
public sealed class VesselComplianceOverviewController(
    IGetVesselComplianceOverviewQuery query)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetVesselComplianceOverviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetVesselComplianceOverviewResponse>> Get(
        Guid vesselId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await query.GetAsync(
                new GetVesselComplianceOverviewRequest { VesselId = vesselId },
                cancellationToken);

            return Ok(response);
        }
        catch (GetVesselComplianceOverviewValidationException exception)
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
