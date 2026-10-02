using System.Security.Claims;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/purchase-orders/{purchaseOrderId:guid}")]
public sealed class PurchaseOrderClosuresController(
    GetPurchaseOrderClosurePreviewQuery getPurchaseOrderClosurePreviewQuery,
    GetPurchaseOrderClosureQuery getPurchaseOrderClosureQuery,
    ClosePurchaseOrderService closePurchaseOrderService)
    : ControllerBase
{
    private const string ReadPolicy = "maintenance.read";
    private const string ManagePolicy = "maintenance.manage";

    // Conflicts that the client can recover from carry the refreshed preview under this extension.
    private const string PreviewExtension = "preview";

    [HttpGet("close/preview")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<PurchaseOrderClosurePreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseOrderClosurePreview>> Preview(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getPurchaseOrderClosurePreviewQuery.GetPreviewAsync(
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
    }

    // Records the entire remaining quantity as closed without delivery; stock and receipts are unchanged.
    [HttpPost("close")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<ClosePurchaseOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClosePurchaseOrderResponse>> Close(
        Guid purchaseOrderId,
        [FromBody] ClosePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await closePurchaseOrderService.CloseAsync(
                purchaseOrderId,
                request,
                actorUserId,
                cancellationToken);

            return Ok(response);
        }
        catch (ClosePurchaseOrderValidationException exception)
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
        catch (PurchaseOrderClosureRequestConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Client request id was already used.",
                Detail = exception.Message,
            });
        }
        catch (PurchaseOrderClosureConcurrencyException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Purchase order was modified concurrently.",
                Detail = exception.Message,
                Extensions = { [PreviewExtension] = exception.RefreshedPreview },
            });
        }
        catch (PurchaseOrderClosureStalePreviewException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Purchase order closure preview is out of date.",
                Detail = exception.Message,
                Extensions = { [PreviewExtension] = exception.RefreshedPreview },
            });
        }
        catch (PurchaseOrderClosureBlockedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Purchase order cannot be closed.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet("closure")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<PurchaseOrderClosureDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseOrderClosureDetails>> GetClosure(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getPurchaseOrderClosureQuery.GetClosureAsync(purchaseOrderId, cancellationToken);

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
        catch (PurchaseOrderClosureNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Purchase order closure not found.",
                Detail = exception.Message,
            });
        }
    }
}
