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
    CreateMaintenanceWorkOrderService createMaintenanceWorkOrderService,
    CompleteMaintenanceWorkOrderService completeMaintenanceWorkOrderService,
    RecordInventoryItemUsageService recordInventoryItemUsageService,
    GetMaintenanceWorkOrdersQuery getMaintenanceWorkOrdersQuery,
    GetWorkOrderPartUsagesQuery getWorkOrderPartUsagesQuery)
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


    [HttpPost("{workOrderId:guid}/completion")]
    [ProducesResponseType<CompleteMaintenanceWorkOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CompleteMaintenanceWorkOrderResponse>> CompleteWorkOrder(
        Guid workOrderId,
        [FromBody] CompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await completeMaintenanceWorkOrderService.CompleteWorkOrderAsync(
                workOrderId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (CompleteMaintenanceWorkOrderValidationException exception)
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
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });
        }
        catch (CompleteMaintenanceWorkOrderConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Maintenance work order cannot be completed from its current status.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{workOrderId:guid}/parts/{partId:guid}/usage")]
    [ProducesResponseType<RecordInventoryItemUsageResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordInventoryItemUsageResponse>> RecordUsage(
        Guid workOrderId,
        Guid partId,
        [FromBody] RecordInventoryItemUsageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await recordInventoryItemUsageService.RecordUsageAsync(
                workOrderId,
                partId,
                request,
                cancellationToken);

            return Created(
                $"/api/maintenance/work-orders/{workOrderId}/parts/{partId}/usage",
                response);
        }
        catch (RecordInventoryItemUsageValidationException exception)
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
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });
        }
        catch (InventoryItemNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Inventory item not found.",
                Detail = exception.Message,
            });
        }
        catch (RecordInventoryItemUsageConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Inventory item usage could not be recorded.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet]
    [ProducesResponseType<GetMaintenanceWorkOrdersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetMaintenanceWorkOrdersResponse>> GetWorkOrders(
        [FromQuery] GetMaintenanceWorkOrdersRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getMaintenanceWorkOrdersQuery.GetWorkOrdersAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetMaintenanceWorkOrdersValidationException exception)
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

    [HttpGet("{workOrderId:guid}/part-usages")]
    [ProducesResponseType<GetWorkOrderPartUsagesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetWorkOrderPartUsagesResponse>> GetPartUsages(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getWorkOrderPartUsagesQuery.GetPartUsagesAsync(workOrderId, cancellationToken);

            return Ok(response);
        }
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });
        }
    }
}
