using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/inventory-items")]
public class InventoryItemsController(GetInventoryItemsQuery getInventoryItemsQuery)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetInventoryItemsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetInventoryItemsResponse>> GetInventoryItems(
        [FromQuery] GetInventoryItemsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getInventoryItemsQuery.GetInventoryItemsAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetInventoryItemsValidationException exception)
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
