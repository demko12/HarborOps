using System.Security.Claims;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/purchase-orders/{purchaseOrderId:guid}")]
public sealed class PurchaseOrderReceiptsController(
    GetPurchaseOrderReceiptPostingPreviewQuery getPurchaseOrderReceiptPostingPreviewQuery,
    GetPurchaseOrderReceiptsQuery getPurchaseOrderReceiptsQuery,
    PostPurchaseOrderReceiptService postPurchaseOrderReceiptService)
    : ControllerBase
{
    private const string ReadPolicy = "maintenance.read";
    private const string ManagePolicy = "maintenance.manage";

    [HttpPost("receipts/preview")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<PurchaseOrderReceiptPostingPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseOrderReceiptPostingPreview>> Preview(
        Guid purchaseOrderId,
        [FromBody] PreviewPurchaseOrderReceiptRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getPurchaseOrderReceiptPostingPreviewQuery.GetPreviewAsync(
                purchaseOrderId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (PreviewPurchaseOrderReceiptValidationException exception)
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
        catch (PurchaseOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Purchase order not found.",
                Detail = exception.Message,
            });
        }
        catch (PurchaseOrderItemsNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Inventory item not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet("receive/preview")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<PurchaseOrderReceiptPostingPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseOrderReceiptPostingPreview>> PreviewRemaining(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getPurchaseOrderReceiptPostingPreviewQuery.GetRemainingPreviewAsync(
                purchaseOrderId,
                cancellationToken);

            return Ok(response);
        }
        catch (PurchaseOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Purchase order not found.",
                Detail = exception.Message,
            });
        }
        catch (PurchaseOrderItemsNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Inventory item not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("receipts")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<PostPurchaseOrderReceiptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PostPurchaseOrderReceiptResponse>> Post(
        Guid purchaseOrderId,
        [FromBody] PostPurchaseOrderReceiptRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await postPurchaseOrderReceiptService.PostAsync(
                purchaseOrderId,
                request,
                actorUserId,
                cancellationToken);

            return Ok(response);
        }
        catch (PostPurchaseOrderReceiptValidationException exception)
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
        catch (PurchaseOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Purchase order not found.",
                Detail = exception.Message,
            });
        }
        catch (PurchaseOrderItemsNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Inventory item not found.",
                Detail = exception.Message,
            });
        }
        catch (PurchaseOrderReceiptRequestConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Client request id was already used.",
                Detail = exception.Message,
            });
        }
        catch (PurchaseOrderReceiptConcurrencyException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Purchase order was modified concurrently.",
                Detail = exception.Message,
            });
        }
        catch (PurchaseOrderReceiptStalePreviewException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Purchase order receipt preview is out of date.",
                Detail = exception.Message,
            });
        }
        catch (PurchaseOrderReceiptPostingBlockedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Purchase order receipt has posting blockers.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet("receipts")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<GetPurchaseOrderReceiptsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetPurchaseOrderReceiptsResponse>> GetReceipts(
        Guid purchaseOrderId,
        [FromQuery] GetPurchaseOrderReceiptsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getPurchaseOrderReceiptsQuery.GetReceiptsAsync(
                purchaseOrderId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (GetPurchaseOrderReceiptsValidationException exception)
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
        catch (PurchaseOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Purchase order not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet("receipts/{receiptId:guid}")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<PurchaseOrderReceiptDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseOrderReceiptDetails>> GetReceipt(
        Guid purchaseOrderId,
        Guid receiptId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getPurchaseOrderReceiptsQuery.GetReceiptAsync(
                purchaseOrderId,
                receiptId,
                cancellationToken);

            return Ok(response);
        }
        catch (PurchaseOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Purchase order not found.",
                Detail = exception.Message,
            });
        }
        catch (PurchaseOrderReceiptNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Receipt not found.",
                Detail = exception.Message,
            });
        }
    }
}
