using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Fleet.Presentation;

[ApiController]
[Route("api/fleet/vessels")]
public sealed class VesselsController(RegisterVesselService registerVesselService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterVesselResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
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
}
