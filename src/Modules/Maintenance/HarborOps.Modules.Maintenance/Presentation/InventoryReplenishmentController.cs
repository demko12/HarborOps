using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/inventory")]
public class InventoryReplenishmentController(
    GetInventoryReplenishmentQuery getInventoryReplenishmentQuery)
    : ControllerBase
{
    private const string ReadPolicy = "maintenance.read";

    [HttpGet("replenishment")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<GetInventoryReplenishmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetInventoryReplenishmentResponse>> GetReplenishment(
        [FromQuery] GetInventoryReplenishmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getInventoryReplenishmentQuery.GetReplenishmentAsync(
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (GetInventoryReplenishmentValidationException exception)
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
