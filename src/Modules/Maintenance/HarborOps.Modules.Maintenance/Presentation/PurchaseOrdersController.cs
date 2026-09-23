using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/purchase-orders")]
public sealed class PurchaseOrdersController(
    CreatePurchaseOrderService createPurchaseOrderService,
    GetPurchaseOrdersQuery getPurchaseOrdersQuery,
    GetPurchaseOrderDetailsQuery getPurchaseOrderDetailsQuery,
    ChangePurchaseOrderStatusService changePurchaseOrderStatusService)
    : ControllerBase
{
    [HttpPost]
    [Authorize("maintenance.manage")]
    [ProducesResponseType<CreatePurchaseOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreatePurchaseOrderResponse>> Create(
        [FromBody] CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await createPurchaseOrderService.CreateAsync(request, cancellationToken);

            return Created($"/api/maintenance/purchase-orders/{response.PurchaseOrderId}", response);
        }
        catch (CreatePurchaseOrderValidationException exception)
        {
            return BadRequest(
                new HttpValidationProblemDetails(
                    exception.Errors.ToDictionary(
                        error => error.Key,
                        error => error.Value,
                        StringComparer.Ordinal))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred.",
                });
        }
        catch (SupplierNotFoundException exception)
        {
            return NotFound(
                new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Supplier not found.",
                    Detail = exception.Message,
                });
        }
        catch (PurchaseOrderItemsNotFoundException exception)
        {
            return NotFound(
                new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Inventory item not found.",
                    Detail = exception.Message,
                });
        }
    }

    [HttpGet]
    [Authorize("maintenance.read")]
    [ProducesResponseType<GetPurchaseOrdersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetPurchaseOrdersResponse>> GetPurchaseOrders(
        [FromQuery] GetPurchaseOrdersRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getPurchaseOrdersQuery.GetPurchaseOrdersAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetPurchaseOrdersValidationException exception)
        {
            return BadRequest(
                new HttpValidationProblemDetails(
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

    [HttpGet("{purchaseOrderId:guid}")]
    [Authorize("maintenance.read")]
    [ProducesResponseType<GetPurchaseOrderDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetPurchaseOrderDetailsResponse>> GetDetails(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getPurchaseOrderDetailsQuery.GetDetailsAsync(purchaseOrderId, cancellationToken);

            return Ok(response);
        }
        catch (PurchaseOrderNotFoundException exception)
        {
            return NotFound(
                new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Purchase order not found.",
                    Detail = exception.Message,
                });
        }
    }

    [HttpPost("{purchaseOrderId:guid}/order")]
    [Authorize("maintenance.manage")]
    [ProducesResponseType<ChangePurchaseOrderStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ChangePurchaseOrderStatusResponse>> MarkOrdered(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        return ExecuteStatusChangeAsync(
            () => changePurchaseOrderStatusService.MarkOrderedAsync(purchaseOrderId, cancellationToken));
    }

    [HttpPost("{purchaseOrderId:guid}/receive")]
    [Authorize("maintenance.manage")]
    [ProducesResponseType<ChangePurchaseOrderStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ChangePurchaseOrderStatusResponse>> Receive(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        return ExecuteStatusChangeAsync(
            () => changePurchaseOrderStatusService.ReceiveAsync(purchaseOrderId, cancellationToken));
    }

    [HttpPost("{purchaseOrderId:guid}/cancel")]
    [Authorize("maintenance.manage")]
    [ProducesResponseType<ChangePurchaseOrderStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ChangePurchaseOrderStatusResponse>> Cancel(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        return ExecuteStatusChangeAsync(
            () => changePurchaseOrderStatusService.CancelAsync(purchaseOrderId, cancellationToken));
    }

    private async Task<ActionResult<ChangePurchaseOrderStatusResponse>> ExecuteStatusChangeAsync(
        Func<Task<ChangePurchaseOrderStatusResponse>> statusChange)
    {
        try
        {
            return Ok(await statusChange());
        }
        catch (PurchaseOrderNotFoundException exception)
        {
            return NotFound(
                new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Purchase order not found.",
                    Detail = exception.Message,
                });
        }
        catch (PurchaseOrderItemsNotFoundException exception)
        {
            return NotFound(
                new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Inventory item not found.",
                    Detail = exception.Message,
                });
        }
        catch (PurchaseOrderConflictException exception)
        {
            return Conflict(
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Purchase order state conflict.",
                    Detail = exception.Message,
                });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Inventory item was modified concurrently.",
                    Detail = "An inventory item on the purchase order was changed by another request. Reload and try again.",
                });
        }
    }
}
