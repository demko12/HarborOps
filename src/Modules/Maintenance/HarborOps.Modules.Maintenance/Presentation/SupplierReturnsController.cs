using System.Security.Claims;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/supplier-returns")]
public sealed class SupplierReturnsController(
    CreateSupplierReturnService createSupplierReturnService,
    GetSupplierReturnsQuery getSupplierReturnsQuery,
    GetSupplierReturnDetailsQuery getSupplierReturnDetailsQuery,
    UpdateSupplierReturnService updateSupplierReturnService,
    GetSupplierReturnPostingPreviewQuery getSupplierReturnPostingPreviewQuery,
    PostSupplierReturnService postSupplierReturnService,
    CancelSupplierReturnService cancelSupplierReturnService)
    : ControllerBase
{
    private const string ReadPolicy = "maintenance.read";
    private const string ManagePolicy = "maintenance.manage";

    [HttpPost]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<SupplierReturnDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierReturnDetails>> Create(
        [FromBody] CreateSupplierReturnRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await createSupplierReturnService.CreateAsync(request, actorUserId, cancellationToken);

            return Created($"/api/maintenance/supplier-returns/{response.SupplierReturnId}", response);
        }
        catch (CreateSupplierReturnValidationException exception)
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
        catch (SupplierReturnPurchaseOrderNotReceivedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Purchase order has not been received.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<GetSupplierReturnsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetSupplierReturnsResponse>> GetSupplierReturns(
        [FromQuery] GetSupplierReturnsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getSupplierReturnsQuery.GetSupplierReturnsAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetSupplierReturnsValidationException exception)
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

    [HttpGet("{supplierReturnId:guid}")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<SupplierReturnDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierReturnDetails>> GetDetails(
        Guid supplierReturnId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getSupplierReturnDetailsQuery.GetDetailsAsync(supplierReturnId, cancellationToken);

            return Ok(response);
        }
        catch (SupplierReturnNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Supplier return not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPut("{supplierReturnId:guid}")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<SupplierReturnDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierReturnDetails>> Update(
        Guid supplierReturnId,
        [FromBody] UpdateSupplierReturnRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await updateSupplierReturnService.UpdateAsync(
                supplierReturnId,
                request,
                actorUserId,
                cancellationToken);

            return Ok(response);
        }
        catch (UpdateSupplierReturnValidationException exception)
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
        catch (SupplierReturnNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Supplier return not found.",
                Detail = exception.Message,
            });
        }
        catch (SupplierReturnNotDraftException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier return can no longer be changed.",
                Detail = exception.Message,
            });
        }
        catch (SupplierReturnConcurrencyException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier return was modified concurrently.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier return was modified concurrently.",
                Detail = "The supplier return was changed by another request. Reload and try again.",
            });
        }
    }

    [HttpGet("{supplierReturnId:guid}/preview")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<SupplierReturnPostingPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierReturnPostingPreview>> Preview(
        Guid supplierReturnId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getSupplierReturnPostingPreviewQuery.GetPreviewAsync(
                supplierReturnId,
                cancellationToken);

            return Ok(response);
        }
        catch (SupplierReturnNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Supplier return not found.",
                Detail = exception.Message,
            });
        }
        catch (SupplierReturnNotDraftException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier return can no longer be changed.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{supplierReturnId:guid}/post")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<PostSupplierReturnResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PostSupplierReturnResponse>> Post(
        Guid supplierReturnId,
        [FromBody] PostSupplierReturnRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await postSupplierReturnService.PostAsync(
                supplierReturnId,
                request,
                actorUserId,
                cancellationToken);

            return Ok(response);
        }
        catch (PostSupplierReturnValidationException exception)
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
        catch (SupplierReturnNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Supplier return not found.",
                Detail = exception.Message,
            });
        }
        catch (SupplierReturnNotDraftException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier return can no longer be changed.",
                Detail = exception.Message,
            });
        }
        catch (SupplierReturnConcurrencyException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier return was modified concurrently.",
                Detail = exception.Message,
            });
        }
        catch (SupplierReturnStalePreviewException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier return posting preview is out of date.",
                Detail = exception.Message,
            });
        }
        catch (SupplierReturnPostingBlockedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier return has posting blockers.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier return posting preview is out of date.",
                Detail = "Inventory was changed by another request while posting. Review a new preview and try again.",
            });
        }
    }

    [HttpPost("{supplierReturnId:guid}/cancel")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<SupplierReturnDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierReturnDetails>> Cancel(
        Guid supplierReturnId,
        [FromBody] CancelSupplierReturnRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await cancelSupplierReturnService.CancelAsync(
                supplierReturnId,
                request,
                actorUserId,
                cancellationToken);

            return Ok(response);
        }
        catch (CancelSupplierReturnValidationException exception)
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
        catch (SupplierReturnNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Supplier return not found.",
                Detail = exception.Message,
            });
        }
        catch (SupplierReturnNotDraftException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier return can no longer be changed.",
                Detail = exception.Message,
            });
        }
        catch (SupplierReturnConcurrencyException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier return was modified concurrently.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Supplier return was modified concurrently.",
                Detail = "The supplier return was changed by another request. Reload and try again.",
            });
        }
    }
}
