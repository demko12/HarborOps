using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/preventive-work")]
public sealed class PreventiveWorkController(
    GetPreventiveWorkForecastQuery getPreventiveWorkForecastQuery,
    GeneratePreventiveWorkService generatePreventiveWorkService)
    : ControllerBase
{
    private const string ReadPolicy = "maintenance.read";
    private const string ManagePolicy = "maintenance.manage";

    [HttpGet("forecast")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<GetPreventiveWorkForecastResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetPreventiveWorkForecastResponse>> GetForecast(
        [FromQuery] GetPreventiveWorkForecastRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getPreventiveWorkForecastQuery.GetForecastAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetPreventiveWorkForecastValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
    }

    [HttpPost("generate/preview")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<PreviewPreventiveWorkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PreviewPreventiveWorkResponse>> Preview(
        [FromBody] GeneratePreventiveWorkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await generatePreventiveWorkService.PreviewAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GeneratePreventiveWorkValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
    }

    [HttpPost("generate")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<GeneratePreventiveWorkResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GeneratePreventiveWorkResponse>> Generate(
        [FromBody] GeneratePreventiveWorkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await generatePreventiveWorkService.GenerateAsync(request, cancellationToken);

            return Created("/api/maintenance/preventive-work/forecast", response);
        }
        catch (GeneratePreventiveWorkValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (PreventiveWorkGenerationConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Preventive work generation conflicted with a concurrent change.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("occurrences/{occurrenceId:guid}/replacement")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<ReplacePreventiveWorkOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReplacePreventiveWorkOrderResponse>> Replace(
        Guid occurrenceId,
        [FromBody] GeneratePreventiveWorkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await generatePreventiveWorkService.ReplaceAsync(
                occurrenceId,
                request,
                cancellationToken);

            return Created($"/api/maintenance/work-orders/{response.WorkOrderId}", response);
        }
        catch (GeneratePreventiveWorkValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (ScheduleOccurrenceNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Schedule occurrence not found.",
                Detail = exception.Message,
            });
        }
        catch (MaintenanceScheduleNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance schedule not found.",
                Detail = exception.Message,
            });
        }
        catch (PreventiveWorkReplacementNotAllowedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Schedule occurrence cannot be replaced from its current state.",
                Detail = exception.Message,
            });
        }
        catch (MaintenanceScheduleNotTimeBasedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Maintenance schedule is not time based.",
                Detail = exception.Message,
            });
        }
        catch (PreventiveWorkScopeMissingException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Maintenance schedule has no equipment scope.",
                Detail = exception.Message,
            });
        }
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
