using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/schedules")]
public sealed class MaintenanceSchedulesController(
    RegisterMaintenanceScheduleService registerMaintenanceScheduleService,
    GetMaintenanceSchedulesQuery getMaintenanceSchedulesQuery,
    GetMaintenanceScheduleDetailsQuery getMaintenanceScheduleDetailsQuery,
    UpdateScheduleNextDueService updateScheduleNextDueService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterMaintenanceScheduleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterMaintenanceScheduleResponse>> Register(
        [FromBody] RegisterMaintenanceScheduleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerMaintenanceScheduleService.RegisterAsync(request, cancellationToken);

            return Created($"/api/maintenance/schedules/{response.Id}", response);
        }
        catch (RegisterMaintenanceScheduleValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (MaintenanceScheduleEquipmentNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Equipment not found.",
                Detail = exception.Message,
            });
        }
        catch (MaintenanceScheduleVesselConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Maintenance schedule equipment spans more than one vessel.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet]
    [ProducesResponseType<GetMaintenanceSchedulesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetMaintenanceSchedulesResponse>> GetSchedules(
        [FromQuery] GetMaintenanceSchedulesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getMaintenanceSchedulesQuery.GetSchedulesAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetMaintenanceSchedulesValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
    }

    [HttpGet("{scheduleId:guid}")]
    [Authorize("maintenance.read")]
    [ProducesResponseType<GetMaintenanceScheduleDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetMaintenanceScheduleDetailsResponse>> GetScheduleDetails(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getMaintenanceScheduleDetailsQuery.GetDetailsAsync(scheduleId, cancellationToken);

            return Ok(response);
        }
        catch (MaintenanceScheduleNotFoundException exception)
        {
            return NotFound(ScheduleNotFound(exception));
        }
    }

    [HttpPost("{scheduleId:guid}/next-due")]
    [Authorize("maintenance.manage")]
    [ProducesResponseType<UpdateScheduleNextDueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UpdateScheduleNextDueResponse>> UpdateNextDue(
        Guid scheduleId,
        [FromBody] UpdateScheduleNextDueRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await updateScheduleNextDueService.UpdateNextDueAsync(
                scheduleId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (UpdateScheduleNextDueValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (MaintenanceScheduleNotFoundException exception)
        {
            return NotFound(ScheduleNotFound(exception));
        }
    }

    private static ProblemDetails ScheduleNotFound(MaintenanceScheduleNotFoundException exception)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Maintenance schedule not found.",
            Detail = exception.Message,
        };
    }

    private static HttpValidationProblemDetails ToValidationProblem(
        IReadOnlyDictionary<string, string[]> errors)
    {
        return new HttpValidationProblemDetails(
            errors.ToDictionary(
                error => error.Key,
                error => error.Value,
                StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };
    }
}
