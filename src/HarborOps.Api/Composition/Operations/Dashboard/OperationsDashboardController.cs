using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Api.Composition.Operations.Dashboard;

[ApiController]
[Authorize]
[Route("api/operations/dashboard")]
public sealed class OperationsDashboardController(OperationsDashboardService dashboardService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<OperationsDashboardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OperationsDashboardResponse>> Get(
        [FromQuery] DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        if (asOf is null)
        {
            return BadRequest(new HttpValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["asOf"] = ["The asOf date is required."],
            })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }

        return Ok(await dashboardService.GetAsync(User, asOf.Value, cancellationToken));
    }
}
