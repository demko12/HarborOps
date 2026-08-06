using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/work-orders")]
public class MaintenanceWorkOrdersController(
    CreateMaintenanceWorkOrderService createMaintenanceWorkOrderService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreateMaintenanceWorkOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateMaintenanceWorkOrderResponse>> Create(
        [FromBody] CreateMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await createMaintenanceWorkOrderService.CreateAsync(request, cancellationToken);

            return Created($"/api/maintenance/work-orders/{response.Id}", response);
        }
        catch (CreateMaintenanceWorkOrderValidationException exception)
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
