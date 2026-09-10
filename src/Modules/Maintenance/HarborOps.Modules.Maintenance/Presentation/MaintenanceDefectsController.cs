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
[Route("api/maintenance/defects")]
public class MaintenanceDefectsController(
    RegisterDefectService registerDefectService,
    TriageDefectService triageDefectService,
    RejectDefectService rejectDefectService,
    ConvertDefectToWorkOrderService convertDefectToWorkOrderService,
    GetDefectsQuery getDefectsQuery,
    GetDefectByIdQuery getDefectByIdQuery)
    : ControllerBase
{
    private const string ReadPolicy = "maintenance.read";
    private const string ManagePolicy = "maintenance.manage";

    [HttpPost]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<DefectDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DefectDetails>> Register(
        [FromBody] RegisterDefectRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var reportedByUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await registerDefectService.RegisterAsync(request, reportedByUserId, cancellationToken);

            return Created($"/api/maintenance/defects/{response.Id}", response);
        }
        catch (RegisterDefectValidationException exception)
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
        catch (DefectVesselNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Vessel not found.",
                Detail = exception.Message,
            });
        }
        catch (DefectEquipmentNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Equipment not found.",
                Detail = exception.Message,
            });
        }
        catch (DefectEquipmentVesselMismatchException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [RegisterDefectValidator.EquipmentIdField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }
    }

    [HttpGet]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<GetDefectsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetDefectsResponse>> GetDefects(
        [FromQuery] GetDefectsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getDefectsQuery.GetDefectsAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetDefectsValidationException exception)
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

    [HttpGet("{defectId:guid}")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<DefectDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DefectDetails>> GetById(
        Guid defectId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getDefectByIdQuery.GetByIdAsync(defectId, cancellationToken);

            return Ok(response);
        }
        catch (DefectNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Defect not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{defectId:guid}/triage")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<DefectDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DefectDetails>> Triage(
        Guid defectId,
        [FromBody] TriageDefectRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await triageDefectService.TriageAsync(defectId, request, actorUserId, cancellationToken);

            return Ok(response);
        }
        catch (TriageDefectValidationException exception)
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
        catch (DefectNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Defect not found.",
                Detail = exception.Message,
            });
        }
        catch (DefectTransitionNotAllowedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Defect status transition is not allowed.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Defect was modified concurrently.",
                Detail = "The defect was changed by another request. Reload and try again.",
            });
        }
    }

    [HttpPost("{defectId:guid}/convert-to-work-order")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<ConvertDefectToWorkOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConvertDefectToWorkOrderResponse>> ConvertToWorkOrder(
        Guid defectId,
        [FromBody] ConvertDefectToWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await convertDefectToWorkOrderService.ConvertAsync(
                defectId,
                request,
                actorUserId,
                cancellationToken);

            return Created($"/api/maintenance/work-orders/{response.WorkOrderId}", response);
        }
        catch (ConvertDefectToWorkOrderValidationException exception)
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
        catch (DefectNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Defect not found.",
                Detail = exception.Message,
            });
        }
        catch (DefectTransitionNotAllowedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Defect status transition is not allowed.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Defect was modified concurrently.",
                Detail = "The defect was changed by another request. Reload and try again.",
            });
        }
    }

    [HttpPost("{defectId:guid}/reject")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<DefectDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DefectDetails>> Reject(
        Guid defectId,
        [FromBody] RejectDefectRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await rejectDefectService.RejectAsync(defectId, request, actorUserId, cancellationToken);

            return Ok(response);
        }
        catch (RejectDefectValidationException exception)
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
        catch (DefectNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Defect not found.",
                Detail = exception.Message,
            });
        }
        catch (DefectTransitionNotAllowedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Defect status transition is not allowed.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Defect was modified concurrently.",
                Detail = "The defect was changed by another request. Reload and try again.",
            });
        }
    }
}
