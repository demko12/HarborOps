using System.Security.Claims;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Presentation;

[ApiController]
[Route("api/cargo")]
public sealed class BillsOfLadingController(
    GetBillsOfLadingQuery getBillsOfLadingQuery,
    GetBillOfLadingDetailsQuery getBillOfLadingDetailsQuery,
    GetBillOfLadingPrintQuery getBillOfLadingPrintQuery,
    CreateBillOfLadingDraftService createBillOfLadingDraftService,
    UpdateBillOfLadingDraftService updateBillOfLadingDraftService,
    IssueBillOfLadingService issueBillOfLadingService,
    VoidBillOfLadingService voidBillOfLadingService)
    : ControllerBase
{
    private const string ManageCargoPolicy = "cargo.manage";
    private const string ReadCargoPolicy = "cargo.read";

    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    [HttpGet("bookings/{bookingId:guid}/bills-of-lading")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<GetBillsOfLadingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetBillsOfLadingResponse>> GetForBooking(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getBillsOfLadingQuery.GetAsync(bookingId, cancellationToken));
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
    }

    [HttpGet("bills-of-lading/{billOfLadingId:guid}")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<GetBillOfLadingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetBillOfLadingResponse>> GetDetails(
        Guid billOfLadingId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getBillOfLadingDetailsQuery.GetAsync(
                billOfLadingId,
                cancellationToken));
        }
        catch (BillOfLadingNotFoundException exception)
        {
            return BillNotFound(exception);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
    }

    [HttpGet("bills-of-lading/{billOfLadingId:guid}/print")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<BillOfLadingPrintModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BillOfLadingPrintModel>> GetPrintModel(
        Guid billOfLadingId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getBillOfLadingPrintQuery.GetAsync(billOfLadingId, cancellationToken));
        }
        catch (BillOfLadingNotFoundException exception)
        {
            return BillNotFound(exception);
        }
    }

    [HttpPost("bookings/{bookingId:guid}/bills-of-lading")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<BillOfLadingDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BillOfLadingDetails>> CreateDraft(
        Guid bookingId,
        [FromBody] SaveBillOfLadingDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var details = await createBillOfLadingDraftService.CreateAsync(
                bookingId,
                actorUserId,
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetDetails),
                new { billOfLadingId = details.BillOfLadingId },
                details);
        }
        catch (BillOfLadingValidationException exception)
        {
            return ValidationFailed(exception);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            return ConflictFor(exception);
        }
    }

    [HttpPut("bills-of-lading/{billOfLadingId:guid}")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<BillOfLadingDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BillOfLadingDetails>> UpdateDraft(
        Guid billOfLadingId,
        [FromBody] SaveBillOfLadingDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await updateBillOfLadingDraftService.UpdateAsync(
                billOfLadingId,
                actorUserId,
                request,
                cancellationToken));
        }
        catch (BillOfLadingValidationException exception)
        {
            return ValidationFailed(exception);
        }
        catch (BillOfLadingNotFoundException exception)
        {
            return BillNotFound(exception);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            return ConflictFor(exception);
        }
    }

    [HttpPost("bills-of-lading/{billOfLadingId:guid}/issue")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<BillOfLadingDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BillOfLadingDetails>> Issue(
        Guid billOfLadingId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await issueBillOfLadingService.IssueAsync(
                billOfLadingId,
                actorUserId,
                cancellationToken));
        }
        catch (BillOfLadingNotFoundException exception)
        {
            return BillNotFound(exception);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            return ConflictFor(exception);
        }
    }

    [HttpPost("bills-of-lading/{billOfLadingId:guid}/void")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<BillOfLadingDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BillOfLadingDetails>> Void(
        Guid billOfLadingId,
        [FromBody] VoidBillOfLadingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await voidBillOfLadingService.VoidAsync(
                billOfLadingId,
                actorUserId,
                request,
                cancellationToken));
        }
        catch (BillOfLadingValidationException exception)
        {
            return ValidationFailed(exception);
        }
        catch (BillOfLadingNotFoundException exception)
        {
            return BillNotFound(exception);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            return ConflictFor(exception);
        }
    }

    private static bool IsConflict(Exception exception)
    {
        return exception
            is DuplicateBillNumberException
            or ContainerNotInBookingException
            or ContainerAlreadyOnBillException
            or BillOfLadingTransitionException
            or DbUpdateConcurrencyException
            || (exception is DbUpdateException update && IsUniqueViolation(update));
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException
        {
            Number: UniqueIndexViolation or UniqueConstraintViolation,
        };
    }

    private bool TryGetActorUserId(out Guid actorUserId)
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out actorUserId);
    }

    private ActionResult ConflictFor(Exception exception)
    {
        return exception switch
        {
            DuplicateBillNumberException duplicate => ConflictProblem(
                "Bill number is already in use.",
                duplicate.Message),
            ContainerNotInBookingException foreign => ConflictProblem(
                "Container does not belong to this booking.",
                foreign.Message),
            ContainerAlreadyOnBillException taken => ConflictProblem(
                "Container is already on another bill of lading.",
                taken.Message),
            BillOfLadingTransitionException transition => ConflictProblem(
                "Bill of lading transition is not allowed.",
                transition.Message),
            _ => ConflictProblem(
                "Bill of lading was modified concurrently.",
                "The bill of lading was changed by another request. Reload and try again."),
        };
    }

    private ActionResult ValidationFailed(BillOfLadingValidationException exception)
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

    private ActionResult BillNotFound(BillOfLadingNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Bill of lading not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult BookingNotFound(CargoBookingNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Cargo booking not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult ConflictProblem(string title, string detail)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = title,
            Detail = detail,
        });
    }
}
