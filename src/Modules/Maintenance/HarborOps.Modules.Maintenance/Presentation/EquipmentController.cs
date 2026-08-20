using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/equipment")]
public sealed class EquipmentController(
    RegisterEquipmentService registerEquipmentService,
    GetEquipmentQuery getEquipmentQuery)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterEquipmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegisterEquipmentResponse>> Register(
        [FromBody] RegisterEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerEquipmentService.RegisterAsync(request, cancellationToken);

            return Created($"/api/maintenance/equipment/{response.Id}", response);
        }
        catch (RegisterEquipmentValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
    }

    [HttpGet]
    [ProducesResponseType<GetEquipmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetEquipmentResponse>> GetEquipment(
        [FromQuery] GetEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getEquipmentQuery.GetEquipmentAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetEquipmentValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
    }

    private static HttpValidationProblemDetails ToValidationProblem(
        IReadOnlyDictionary<string, string[]> errors)
    {
        return new HttpValidationProblemDetails(
            errors.ToDictionary(
                error => error.Key,
                error => error.Value,
                StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };
    }
}
