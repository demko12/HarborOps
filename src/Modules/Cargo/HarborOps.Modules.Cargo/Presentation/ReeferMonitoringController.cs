using System.Security.Claims;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Presentation;

[ApiController]
[Route("api/cargo/containers/{containerId:guid}/reefer")]
public sealed class ReeferMonitoringController(
    GetReeferMonitoringProfileQuery getReeferMonitoringProfileQuery,
    SaveReeferMonitoringProfileService saveReeferMonitoringProfileService,
    GetReeferTemperatureReadingsQuery getReeferTemperatureReadingsQuery,
    RecordReeferTemperatureReadingService recordReeferTemperatureReadingService,
    CorrectReeferTemperatureReadingService correctReeferTemperatureReadingService,
    VoidReeferTemperatureReadingService voidReeferTemperatureReadingService,
    GetReeferExcursionsQuery getReeferExcursionsQuery,
    ResolveReeferExcursionService resolveReeferExcursionService)
    : ControllerBase
{
    private const string ManageCargoPolicy = "cargo.manage";
    private const string ReadCargoPolicy = "cargo.read";

    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    [HttpGet("profile")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<GetReeferMonitoringProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetReeferMonitoringProfileResponse>> GetProfile(
        Guid containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getReeferMonitoringProfileQuery.GetAsync(
                containerId,
                cancellationToken);

            return Ok(response);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (ContainerIsNotReeferException)
        {
            return NotAReefer();
        }
    }

    [HttpPut("profile")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<ReeferMonitoringProfileDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReeferMonitoringProfileDetails>> SaveProfile(
        Guid containerId,
        [FromBody] SaveReeferMonitoringProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var details = await saveReeferMonitoringProfileService.SaveAsync(
                containerId,
                actorUserId,
                request,
                cancellationToken);

            return Ok(details);
        }
        catch (ReeferMonitoringValidationException exception)
        {
            return ValidationFailed(exception.Errors);
        }
        catch (ReeferThresholdsNotValidException exception)
        {
            return ThresholdsNotValid(exception);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (ContainerIsNotReeferException)
        {
            return NotAReefer();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ConcurrentChange();
        }
    }

    [HttpGet("readings")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<GetReeferTemperatureReadingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetReeferTemperatureReadingsResponse>> GetReadings(
        Guid containerId,
        [FromQuery] GetReeferTemperatureReadingsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getReeferTemperatureReadingsQuery.GetAsync(
                containerId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (ReeferMonitoringValidationException exception)
        {
            return ValidationFailed(exception.Errors);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (ContainerIsNotReeferException)
        {
            return NotAReefer();
        }
    }

    [HttpPost("readings")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<RecordReeferTemperatureReadingResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordReeferTemperatureReadingResponse>> RecordReading(
        Guid containerId,
        [FromBody] RecordReeferTemperatureReadingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await recordReeferTemperatureReadingService.RecordAsync(
                containerId,
                actorUserId,
                request,
                cancellationToken);

            return CreatedAtAction(nameof(GetReadings), new { containerId }, response);
        }
        catch (ReeferMonitoringValidationException exception)
        {
            return ValidationFailed(exception.Errors);
        }
        catch (ReeferProfileNotFoundException)
        {
            return ValidationFailed(ReeferMonitoringValidator.ProfileMissingError());
        }
        catch (ReeferReadingNotValidException exception)
        {
            return ReadingNotValid(exception);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (ContainerIsNotReeferException)
        {
            return NotAReefer();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ConcurrentChange();
        }
    }

    [HttpPost("readings/{readingId:guid}/correct")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<RecordReeferTemperatureReadingResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordReeferTemperatureReadingResponse>> CorrectReading(
        Guid containerId,
        Guid readingId,
        [FromBody] CorrectReeferTemperatureReadingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await correctReeferTemperatureReadingService.CorrectAsync(
                containerId,
                readingId,
                actorUserId,
                request,
                cancellationToken);

            return CreatedAtAction(nameof(GetReadings), new { containerId }, response);
        }
        catch (ReeferMonitoringValidationException exception)
        {
            return ValidationFailed(exception.Errors);
        }
        catch (ReeferReadingNotValidException exception)
        {
            return ReadingNotValid(exception);
        }
        catch (ReeferReadingNotFoundException exception)
        {
            return ReadingNotFound(exception);
        }
        catch (ReeferReadingTransitionException exception)
        {
            return ConflictProblem(
                "Reefer temperature reading is no longer effective.",
                exception.Message);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (ContainerIsNotReeferException)
        {
            return NotAReefer();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ConcurrentChange();
        }
    }

    [HttpPost("readings/{readingId:guid}/void")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<ReeferTemperatureReadingListItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReeferTemperatureReadingListItem>> VoidReading(
        Guid containerId,
        Guid readingId,
        [FromBody] VoidReeferTemperatureReadingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var reading = await voidReeferTemperatureReadingService.VoidAsync(
                containerId,
                readingId,
                actorUserId,
                request,
                cancellationToken);

            return Ok(reading);
        }
        catch (ReeferMonitoringValidationException exception)
        {
            return ValidationFailed(exception.Errors);
        }
        catch (ReeferReadingNotFoundException exception)
        {
            return ReadingNotFound(exception);
        }
        catch (ReeferReadingTransitionException exception)
        {
            return ConflictProblem(
                "Reefer temperature reading is no longer effective.",
                exception.Message);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (ContainerIsNotReeferException)
        {
            return NotAReefer();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ConcurrentChange();
        }
    }

    [HttpGet("excursions")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<GetReeferExcursionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetReeferExcursionsResponse>> GetExcursions(
        Guid containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getReeferExcursionsQuery.GetAsync(containerId, cancellationToken);

            return Ok(response);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (ContainerIsNotReeferException)
        {
            return NotAReefer();
        }
    }

    [HttpGet("excursions/{excursionId:guid}")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<ReeferExcursionListItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReeferExcursionListItem>> GetExcursion(
        Guid containerId,
        Guid excursionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var excursion = await getReeferExcursionsQuery.GetDetailsAsync(
                containerId,
                excursionId,
                cancellationToken);

            return Ok(excursion);
        }
        catch (ReeferExcursionNotFoundException exception)
        {
            return ExcursionNotFound(exception);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (ContainerIsNotReeferException)
        {
            return NotAReefer();
        }
    }

    [HttpPost("excursions/{excursionId:guid}/resolve")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<ReeferExcursionListItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReeferExcursionListItem>> ResolveExcursion(
        Guid containerId,
        Guid excursionId,
        [FromBody] ResolveReeferExcursionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var excursion = await resolveReeferExcursionService.ResolveAsync(
                containerId,
                excursionId,
                actorUserId,
                request,
                cancellationToken);

            return Ok(excursion);
        }
        catch (ReeferMonitoringValidationException exception)
        {
            return ValidationFailed(exception.Errors);
        }
        catch (ReeferExcursionNotFoundException exception)
        {
            return ExcursionNotFound(exception);
        }
        catch (ReeferExcursionTransitionException exception)
        {
            return ConflictProblem(
                "Temperature excursion is no longer open.",
                exception.Message);
        }
        catch (ContainerNotFoundException exception)
        {
            return ContainerNotFound(exception);
        }
        catch (ContainerIsNotReeferException)
        {
            return NotAReefer();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ConcurrentChange();
        }
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

    private ActionResult ValidationFailed(IReadOnlyDictionary<string, string[]> errors)
    {
        return BadRequest(new HttpValidationProblemDetails(
            errors.ToDictionary(
                error => error.Key,
                error => error.Value,
                StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        });
    }

    private ActionResult NotAReefer()
    {
        return ValidationFailed(ReeferMonitoringValidator.ContainerIsNotReeferError());
    }

    private ActionResult ThresholdsNotValid(ReeferThresholdsNotValidException exception)
    {
        return ValidationFailed(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [ReeferMonitoringValidator.MinimumCelsiusField] = [exception.Message],
        });
    }

    private ActionResult ReadingNotValid(ReeferReadingNotValidException exception)
    {
        return ValidationFailed(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [ReeferMonitoringValidator.TemperatureCelsiusField] = [exception.Message],
        });
    }

    private ActionResult ContainerNotFound(ContainerNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Container not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult ReadingNotFound(ReeferReadingNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Reefer temperature reading not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult ExcursionNotFound(ReeferExcursionNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Temperature excursion not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult ConcurrentChange()
    {
        return ConflictProblem(
            "Reefer monitoring was modified concurrently.",
            "The reefer monitoring record was changed by another request. Reload and try again.");
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
