using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace HarborOps.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/maintenance/work-orders")]
public class MaintenanceWorkOrdersController(
    CreateMaintenanceWorkOrderService createMaintenanceWorkOrderService,
    CompleteMaintenanceWorkOrderService completeMaintenanceWorkOrderService,
    RecordInventoryItemUsageService recordInventoryItemUsageService,
    GetMaintenanceWorkOrdersQuery getMaintenanceWorkOrdersQuery,
    GetWorkOrderPartUsagesQuery getWorkOrderPartUsagesQuery,
    GetMaintenanceWorkOrderDetailsQuery getMaintenanceWorkOrderDetailsQuery,
    ReplaceWorkOrderEquipmentService replaceWorkOrderEquipmentService,
    StartMaintenanceWorkOrderService startMaintenanceWorkOrderService,
    CancelMaintenanceWorkOrderService cancelMaintenanceWorkOrderService,
    GetWorkOrderPartReservationsQuery getWorkOrderPartReservationsQuery,
    ReplaceWorkOrderPartReservationsService replaceWorkOrderPartReservationsService,
    ReleaseWorkOrderPartReservationsService releaseWorkOrderPartReservationsService)
    : ControllerBase
{

    private const string ReadPolicy = "maintenance.read";
    private const string ManagePolicy = "maintenance.manage";
    [HttpPost]
    [ProducesResponseType<CreateMaintenanceWorkOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateMaintenanceWorkOrderResponse>> Create(
        [FromBody] CreateMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await createMaintenanceWorkOrderService.CreateAsync(request, cancellationToken);

            return Created($"/api/maintenance/work-orders/{response.Id}", response);
        }
        catch (CreateMaintenanceWorkOrderValidationException exception)
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


    [HttpPost("{workOrderId:guid}/completion")]
    [ProducesResponseType<CompleteMaintenanceWorkOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CompleteMaintenanceWorkOrderResponse>> CompleteWorkOrder(
        Guid workOrderId,
        [FromBody] CompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await completeMaintenanceWorkOrderService.CompleteWorkOrderAsync(
                workOrderId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (CompleteMaintenanceWorkOrderValidationException exception)
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
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });
        }
        catch (CompleteMaintenanceWorkOrderConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Maintenance work order cannot be completed from its current status.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{workOrderId:guid}/parts/{partId:guid}/usage")]
    [ProducesResponseType<RecordInventoryItemUsageResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordInventoryItemUsageResponse>> RecordUsage(
        Guid workOrderId,
        Guid partId,
        [FromBody] RecordInventoryItemUsageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await recordInventoryItemUsageService.RecordUsageAsync(
                workOrderId,
                partId,
                request,
                cancellationToken);

            return Created(
                $"/api/maintenance/work-orders/{workOrderId}/parts/{partId}/usage",
                response);
        }
        catch (RecordInventoryItemUsageValidationException exception)
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
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });
        }
        catch (InventoryItemNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Inventory item not found.",
                Detail = exception.Message,
            });
        }
        catch (RecordInventoryItemUsageConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Inventory item usage could not be recorded.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet]
    [ProducesResponseType<GetMaintenanceWorkOrdersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetMaintenanceWorkOrdersResponse>> GetWorkOrders(
        [FromQuery] GetMaintenanceWorkOrdersRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getMaintenanceWorkOrdersQuery.GetWorkOrdersAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetMaintenanceWorkOrdersValidationException exception)
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

    [HttpGet("{workOrderId:guid}/part-usages")]
    [ProducesResponseType<GetWorkOrderPartUsagesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetWorkOrderPartUsagesResponse>> GetPartUsages(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getWorkOrderPartUsagesQuery.GetPartUsagesAsync(workOrderId, cancellationToken);

            return Ok(response);
        }
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet("{workOrderId:guid}")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<GetMaintenanceWorkOrderDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetMaintenanceWorkOrderDetailsResponse>> GetDetails(
          Guid workOrderId,
          CancellationToken cancellationToken)
    {
        try
        {
            var response = await getMaintenanceWorkOrderDetailsQuery.GetDetailsAsync(
                workOrderId,
                cancellationToken);

            return Ok(response);
        }
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });

        }
    }

    [HttpPut("{workOrderId:guid}/equipment")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<ReplaceWorkOrderEquipmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReplaceWorkOrderEquipmentResponse>> ReplaceEquipment(
        Guid workOrderId,
        [FromBody] ReplaceWorkOrderEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await replaceWorkOrderEquipmentService.ReplaceEquipmentAsync(
                workOrderId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (ReplaceWorkOrderEquipmentValidationException exception)
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
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });

        }
        catch (WorkOrderEquipmentNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Equipment not found.",
                Detail = exception.Message,
            });
        }
        catch (WorkOrderEquipmentVesselMismatchException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Equipment does not belong to the work order vessel.",
                Detail = exception.Message,
            });
        }
        catch (WorkOrderEquipmentChangeNotAllowedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Work order equipment cannot be changed from its current status.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{workOrderId:guid}/start")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<MaintenanceWorkOrderTransitionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MaintenanceWorkOrderTransitionResponse>> Start(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await startMaintenanceWorkOrderService.StartAsync(workOrderId, cancellationToken);

            return Ok(response);
        }
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });

        }
        catch (WorkOrderPartReservationShortageException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Maintenance work order has unreserved parts.",
                Detail = exception.Message,
            });
        }
        catch (WorkOrderTransitionNotAllowedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Maintenance work order status transition is not allowed.",
                Detail = exception.Message,
            });

        }
    }

    [HttpPost("{workOrderId:guid}/cancel")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<MaintenanceWorkOrderTransitionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MaintenanceWorkOrderTransitionResponse>> Cancel(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await cancelMaintenanceWorkOrderService.CancelAsync(workOrderId, cancellationToken);

            return Ok(response);
        }
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });

        }
        catch (WorkOrderTransitionNotAllowedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Maintenance work order status transition is not allowed.",
                Detail = exception.Message,
            });

        }
    }

    [HttpGet("{workOrderId:guid}/part-reservations")]
    [Authorize(ReadPolicy)]
    [ProducesResponseType<GetWorkOrderPartReservationsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetWorkOrderPartReservationsResponse>> GetPartReservations(
    Guid workOrderId,
    CancellationToken cancellationToken)
    {
        try
        {
            var response = await getWorkOrderPartReservationsQuery.GetReservationsAsync(
                workOrderId,
                cancellationToken);

            return Ok(response);
        }
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPut("{workOrderId:guid}/part-reservations")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType<ReplaceWorkOrderPartReservationsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReplaceWorkOrderPartReservationsResponse>> ReplacePartReservations(
        Guid workOrderId,
        [FromBody] ReplaceWorkOrderPartReservationsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await replaceWorkOrderPartReservationsService.ReplaceReservationsAsync(
                workOrderId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (ReplaceWorkOrderPartReservationsValidationException exception)
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
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });
        }
        catch (WorkOrderPartReservationItemsNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Inventory item not found.",
                Detail = exception.Message,
            });
        }
        catch (WorkOrderReservationChangeNotAllowedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Work order part reservations cannot be changed from its current status.",
                Detail = exception.Message,
            });
        }
        catch (WorkOrderPartReservationConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Work order part reservations could not be replaced.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{workOrderId:guid}/part-reservations/release")]
    [Authorize(ManagePolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReleasePartReservations(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            await releaseWorkOrderPartReservationsService.ReleaseAsync(workOrderId, cancellationToken);

            return NoContent();
        }
        catch (MaintenanceWorkOrderNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Maintenance work order not found.",
                Detail = exception.Message,
            });
        }
        catch (WorkOrderReservationReleaseNotAllowedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Work order part reservations cannot be released from its current status.",
                Detail = exception.Message,
            });
        }
    }
}
