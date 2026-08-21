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
[Route("api/maintenance/suppliers")]
public sealed class SuppliersController(
    RegisterSupplierService registerSupplierService,
    GetSuppliersQuery getSuppliersQuery)
    : ControllerBase
{
    [HttpPost]
    [Authorize("maintenance.manage")]
    [ProducesResponseType<RegisterSupplierResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegisterSupplierResponse>> Register(
        [FromBody] RegisterSupplierRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerSupplierService.RegisterAsync(request, cancellationToken);

            return Created($"/api/maintenance/suppliers/{response.SupplierId}", response);
        }
        catch (RegisterSupplierValidationException exception)
        {
            return BadRequest(
        new HttpValidationProblemDetails(
            exception.Errors.ToDictionary(
                error => error.Key,
                error => error.Value,
                StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred."
        });
        }
    }

    [HttpGet]
    [Authorize("maintenance.read")]
    [ProducesResponseType<GetSuppliersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetSuppliersResponse>> GetSuppliers(
        [FromQuery] GetSuppliersRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getSuppliersQuery.GetSuppliersAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetSuppliersValidationException exception)
        {
            return BadRequest(
        new HttpValidationProblemDetails(
            exception.Errors.ToDictionary(
                error => error.Key,
                error => error.Value,
                StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred."
        });
        }
    }
}
