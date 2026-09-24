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
[Route("api/maintenance/stocktakes")]
public sealed class StocktakesController(
    CreateStocktakeService createStocktakeService,
    GetStocktakesQuery getStocktakesQuery,
    GetStocktakeDetailsQuery getStocktakeDetailsQuery,
    UpdateStocktakeCountsService updateStocktakeCountsService,
    RefreshStocktakeLinesService refreshStocktakeLinesService,
    GetStocktakePostingPreviewQuery getStocktakePostingPreviewQuery,
    PostStocktakeService postStocktakeService,
    CancelStocktakeService cancelStocktakeService)
    : ControllerBase
{
    private const string ReadPolicy = "maintenance.read";
    private const string ManagePolicy = "maintenance.manage";

    [HttpPost]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<StocktakeDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StocktakeDetails>> Create(
        [FromBody] CreateStocktakeRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await createStocktakeService.CreateAsync(request, actorUserId, cancellationToken);

            return Created($"/api/maintenance/stocktakes/{response.StocktakeId}", response);
        }
        catch (CreateStocktakeValidationException exception)
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
        catch (StocktakeItemsNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Inventory item not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<GetStocktakesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetStocktakesResponse>> GetStocktakes(
        [FromQuery] GetStocktakesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getStocktakesQuery.GetStocktakesAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetStocktakesValidationException exception)
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

    [HttpGet("{stocktakeId:guid}")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<StocktakeDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StocktakeDetails>> GetDetails(
        Guid stocktakeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getStocktakeDetailsQuery.GetDetailsAsync(stocktakeId, cancellationToken);

            return Ok(response);
        }
        catch (StocktakeNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Stocktake not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPut("{stocktakeId:guid}/counts")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<StocktakeDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StocktakeDetails>> UpdateCounts(
        Guid stocktakeId,
        [FromBody] UpdateStocktakeCountsRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await updateStocktakeCountsService.UpdateCountsAsync(
                stocktakeId,
                request,
                actorUserId,
                cancellationToken);

            return Ok(response);
        }
        catch (UpdateStocktakeCountsValidationException exception)
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
        catch (StocktakeNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Stocktake not found.",
                Detail = exception.Message,
            });
        }
        catch (StocktakeNotDraftException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake can no longer be changed.",
                Detail = exception.Message,
            });
        }
        catch (StocktakeConcurrencyException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake was modified concurrently.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake was modified concurrently.",
                Detail = "The stocktake was changed by another request. Reload and try again.",
            });
        }
    }

    [HttpPost("{stocktakeId:guid}/refresh")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<StocktakeDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StocktakeDetails>> Refresh(
        Guid stocktakeId,
        [FromBody] RefreshStocktakeLinesRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await refreshStocktakeLinesService.RefreshAsync(
                stocktakeId,
                request,
                actorUserId,
                cancellationToken);

            return Ok(response);
        }
        catch (RefreshStocktakeLinesValidationException exception)
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
        catch (StocktakeNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Stocktake not found.",
                Detail = exception.Message,
            });
        }
        catch (StocktakeNotDraftException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake can no longer be changed.",
                Detail = exception.Message,
            });
        }
        catch (StocktakeConcurrencyException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake was modified concurrently.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake was modified concurrently.",
                Detail = "The stocktake was changed by another request. Reload and try again.",
            });
        }
    }

    [HttpGet("{stocktakeId:guid}/preview")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<StocktakePostingPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StocktakePostingPreview>> Preview(
        Guid stocktakeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getStocktakePostingPreviewQuery.GetPreviewAsync(stocktakeId, cancellationToken);

            return Ok(response);
        }
        catch (StocktakeNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Stocktake not found.",
                Detail = exception.Message,
            });
        }
        catch (StocktakeNotDraftException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake can no longer be changed.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{stocktakeId:guid}/post")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<PostStocktakeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PostStocktakeResponse>> Post(
        Guid stocktakeId,
        [FromBody] PostStocktakeRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await postStocktakeService.PostAsync(stocktakeId, request, actorUserId, cancellationToken);

            return Ok(response);
        }
        catch (PostStocktakeValidationException exception)
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
        catch (StocktakeNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Stocktake not found.",
                Detail = exception.Message,
            });
        }
        catch (StocktakeAlreadyPostedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake has already been posted.",
                Detail = exception.Message,
            });
        }
        catch (StocktakeNotDraftException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake can no longer be changed.",
                Detail = exception.Message,
            });
        }
        catch (StocktakeConcurrencyException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake was modified concurrently.",
                Detail = exception.Message,
            });
        }
        catch (StocktakeStalePreviewException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake posting preview is out of date.",
                Detail = exception.Message,
            });
        }
        catch (StocktakePostingBlockedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake has posting blockers.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake posting preview is out of date.",
                Detail = "Inventory was changed by another request while posting. Review a new preview and try again.",
            });
        }
    }

    [HttpPost("{stocktakeId:guid}/cancel")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<StocktakeDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StocktakeDetails>> Cancel(
        Guid stocktakeId,
        [FromBody] CancelStocktakeRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await cancelStocktakeService.CancelAsync(stocktakeId, request, actorUserId, cancellationToken);

            return Ok(response);
        }
        catch (CancelStocktakeValidationException exception)
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
        catch (StocktakeNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Stocktake not found.",
                Detail = exception.Message,
            });
        }
        catch (StocktakeNotDraftException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake can no longer be changed.",
                Detail = exception.Message,
            });
        }
        catch (StocktakeConcurrencyException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake was modified concurrently.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stocktake was modified concurrently.",
                Detail = "The stocktake was changed by another request. Reload and try again.",
            });
        }
    }
}
