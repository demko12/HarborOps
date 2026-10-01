using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Presentation;

[ApiController]
[Route("api/cargo/bookings/{bookingId:guid}/discrepancies")]
public sealed class CargoDiscrepanciesController(
    GetCargoDiscrepanciesQuery getCargoDiscrepanciesQuery,
    RegisterCargoDiscrepancyService registerCargoDiscrepancyService,
    CargoDiscrepancyWorkflowService cargoDiscrepancyWorkflowService,
    CargoCompletionBlockersQuery cargoCompletionBlockersQuery)
    : ControllerBase
{
    private const string ManageCargoPolicy = "cargo.manage";
    private const string ReadCargoPolicy = "cargo.read";

    [HttpGet]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<GetCargoDiscrepanciesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetCargoDiscrepanciesResponse>> GetDiscrepancies(
        Guid bookingId,
        [FromQuery] GetCargoDiscrepanciesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getCargoDiscrepanciesQuery.GetAsync(bookingId, request, cancellationToken));
        }
        catch (CargoDiscrepancyValidationException exception)
        {
            return ValidationFailed(exception.Errors);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
    }

    [HttpGet("completion-blockers")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<CargoCompletionBlockers>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CargoCompletionBlockers>> GetCompletionBlockers(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await cargoCompletionBlockersQuery.GetAsync(bookingId, cancellationToken));
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
    }

    [HttpGet("{discrepancyId:guid}")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<CargoDiscrepancyDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CargoDiscrepancyDetails>> GetDiscrepancy(
        Guid bookingId,
        Guid discrepancyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getCargoDiscrepanciesQuery.GetDetailsAsync(bookingId, discrepancyId, cancellationToken));
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (CargoDiscrepancyNotFoundException exception)
        {
            return DiscrepancyNotFound(exception);
        }
    }

    [HttpPost]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<CargoDiscrepancyDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargoDiscrepancyDetails>> Register(
        Guid bookingId,
        [FromBody] RegisterCargoDiscrepancyRequest request,
        CancellationToken cancellationToken)
    {
        if (!CargoDiscrepancyActorClaims.TryRead(User, out var actor))
        {
            return Unauthorized();
        }

        try
        {
            var details = await registerCargoDiscrepancyService.RegisterAsync(
                bookingId,
                actor,
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetDiscrepancy),
                new { bookingId, discrepancyId = details.Discrepancy.Id },
                details);
        }
        catch (CargoDiscrepancyValidationException exception)
        {
            return ValidationFailed(exception.Errors);
        }
        catch (CargoDiscrepancyContainerRequiredException exception)
        {
            return ValidationFailed(CargoDiscrepancyValidator.SingleError(
                CargoDiscrepancyValidator.ContainerIdField,
                exception.Message));
        }
        catch (CargoDiscrepancyContainerMismatchException exception)
        {
            return ValidationFailed(CargoDiscrepancyValidator.SingleError(
                CargoDiscrepancyValidator.ContainerIdField,
                exception.Message));
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (CargoBookingCompletedException exception)
        {
            return ConflictProblem("Cargo booking is completed.", exception.Message);
        }
        catch (CargoDiscrepancyRegistrationNotAllowedException exception)
        {
            return ConflictProblem("Discrepancies cannot be registered yet.", exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
    }

    [HttpPost("{discrepancyId:guid}/investigation")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<CargoDiscrepancyDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<CargoDiscrepancyDetails>> StartInvestigation(
        Guid bookingId,
        Guid discrepancyId,
        CancellationToken cancellationToken)
    {
        return MutateAsync(actor => cargoDiscrepancyWorkflowService.StartInvestigationAsync(
            bookingId,
            discrepancyId,
            actor,
            cancellationToken));
    }

    [HttpPost("{discrepancyId:guid}/notes")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<CargoDiscrepancyDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<CargoDiscrepancyDetails>> AddNote(
        Guid bookingId,
        Guid discrepancyId,
        [FromBody] AddCargoDiscrepancyNoteRequest request,
        CancellationToken cancellationToken)
    {
        return MutateAsync(actor => cargoDiscrepancyWorkflowService.AddNoteAsync(
            bookingId,
            discrepancyId,
            actor,
            request,
            cancellationToken));
    }

    [HttpPost("{discrepancyId:guid}/resolution")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<CargoDiscrepancyDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<CargoDiscrepancyDetails>> Resolve(
        Guid bookingId,
        Guid discrepancyId,
        [FromBody] ResolveCargoDiscrepancyRequest request,
        CancellationToken cancellationToken)
    {
        return MutateAsync(actor => cargoDiscrepancyWorkflowService.ResolveAsync(
            bookingId,
            discrepancyId,
            actor,
            request,
            cancellationToken));
    }

    [HttpPost("{discrepancyId:guid}/void")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<CargoDiscrepancyDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<CargoDiscrepancyDetails>> Void(
        Guid bookingId,
        Guid discrepancyId,
        [FromBody] VoidCargoDiscrepancyRequest request,
        CancellationToken cancellationToken)
    {
        return MutateAsync(actor => cargoDiscrepancyWorkflowService.VoidAsync(
            bookingId,
            discrepancyId,
            actor,
            request,
            cancellationToken));
    }

    private async Task<ActionResult<CargoDiscrepancyDetails>> MutateAsync(
        Func<CargoDiscrepancyActor, Task<CargoDiscrepancyDetails>> mutate)
    {
        if (!CargoDiscrepancyActorClaims.TryRead(User, out var actor))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await mutate(actor));
        }
        catch (CargoDiscrepancyValidationException exception)
        {
            return ValidationFailed(exception.Errors);
        }
        catch (CargoDiscrepancyTextRequiredException exception)
        {
            return ValidationFailed(CargoDiscrepancyValidator.SingleError(
                TextFieldFor(exception.RequestedAction),
                exception.Message));
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (CargoDiscrepancyNotFoundException exception)
        {
            return DiscrepancyNotFound(exception);
        }
        catch (CargoDiscrepancyTransitionNotAllowedException exception)
        {
            return ConflictProblem("Discrepancy transition is not allowed.", exception.Message);
        }
        catch (CargoDiscrepancySourceExcursionOpenException exception)
        {
            return ConflictProblem("The linked temperature excursion is still open.", exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
    }

    private static string TextFieldFor(string requestedAction)
    {
        return requestedAction switch
        {
            CargoDiscrepancyTransitionNotAllowedException.AddNoteAction => CargoDiscrepancyValidator.NoteField,
            CargoDiscrepancyTransitionNotAllowedException.VoidAction => CargoDiscrepancyValidator.ReasonField,
            _ => CargoDiscrepancyValidator.SummaryField,
        };
    }

    private ActionResult ValidationFailed(IReadOnlyDictionary<string, string[]> errors)
    {
        return BadRequest(new HttpValidationProblemDetails(
            errors.ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
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

    private ActionResult DiscrepancyNotFound(CargoDiscrepancyNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Cargo discrepancy not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult ConcurrentChange()
    {
        return ConflictProblem(
            "Cargo discrepancy was modified concurrently.",
            "The discrepancy or its booking was changed by another request. Reload and try again.");
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
