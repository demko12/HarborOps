using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Compliance.Presentation;

[ApiController]
[Route("api/compliance/psc-inspections")]
public sealed class PscInspectionsController(
    RegisterPscInspectionService registerPscInspectionService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterPscInspectionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegisterPscInspectionResponse>> Register(
        [FromBody] RegisterPscInspectionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerPscInspectionService.RegisterAsync(request, cancellationToken);

            return Created($"/api/compliance/psc-inspections/{response.Id}", response);
        }
        catch (RegisterPscInspectionValidationException exception)
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
