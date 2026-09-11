using System.Security.Claims;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Voyages.Presentation;

[ApiController]
[Route("api/voyages")]
public sealed class VoyagesController(
    ScheduleVoyageService scheduleVoyageService,
    RecordVoyageDepartureService recordVoyageDepartureService,
    RecordVoyageArrivalService recordVoyageArrivalService,
    IGetVoyagesQuery getVoyagesQuery,
    PlanVoyageItineraryService planVoyageItineraryService,
    IGetVoyageItineraryQuery getVoyageItineraryQuery,
    AssignBerthService assignBerthService,
    RecordStopArrivalService recordStopArrivalService,
    RecordStopDepartureService recordStopDepartureService,
    RecordDelayService recordDelayService,
    DelayImpactPreviewService delayImpactPreviewService,
    AmendVoyageScheduleService amendVoyageScheduleService,
    CancelVoyageService cancelVoyageService,
    IGetVoyageScheduleChangesQuery getVoyageScheduleChangesQuery,
    IGetBerthAvailabilityQuery getBerthAvailabilityQuery,
    ReplaceBerthAllocationService replaceBerthAllocationService,
    IGetBerthAllocationHistoryQuery getBerthAllocationHistoryQuery,
    IGetPortCallClearanceQuery getPortCallClearanceQuery,
    AddPortCallClearanceItemService addPortCallClearanceItemService,
    UpdatePortCallClearanceItemService updatePortCallClearanceItemService,
    ClearPortCallClearanceItemService clearPortCallClearanceItemService,
    WaivePortCallClearanceItemService waivePortCallClearanceItemService,
    CompletePortCallClearanceService completePortCallClearanceService,
    ReopenPortCallClearanceService reopenPortCallClearanceService)
    : ControllerBase
{
    private const string ManageVoyagesPolicy = "voyages.manage";

    private const string ReadVoyagesPolicy = "voyages.read";

    [HttpGet]
    [ProducesResponseType<GetVoyagesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetVoyagesResponse>> Get(
        [FromQuery] GetVoyagesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getVoyagesQuery.GetVoyagesAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetVoyagesValidationException exception)
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

    [HttpPost("{voyageId:guid}/departure")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<RecordVoyageDepartureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordVoyageDepartureResponse>> RecordDeparture(
        Guid voyageId,
        [FromBody] RecordVoyageDepartureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await recordVoyageDepartureService.RecordDepartureAsync(
                voyageId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (RecordVoyageDepartureValidationException exception)
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
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageNotPlannedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage cannot depart from its current status.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{voyageId:guid}/arrival")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<RecordVoyageArrivalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordVoyageArrivalResponse>> RecordArrival(
        Guid voyageId,
        [FromBody] RecordVoyageArrivalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await recordVoyageArrivalService.RecordArrivalAsync(
                voyageId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (RecordVoyageArrivalValidationException exception)
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
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageNotInProgressException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage cannot arrive from its current status.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost]
    [ProducesResponseType<ScheduleVoyageResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScheduleVoyageResponse>> Schedule(
        [FromBody] ScheduleVoyageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await scheduleVoyageService.ScheduleAsync(request, cancellationToken);

            return Created($"/api/voyages/{response.Id}", response);
        }
        catch (ScheduleVoyageValidationException exception)
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
        catch (DuplicateVoyageNumberException exception)
        {
            return Conflict(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [ScheduleVoyageValidator.VoyageNumberField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A voyage with this voyage number already exists.",
            });
        }
    }

    [HttpPost("{voyageId:guid}/itinerary")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<PlanVoyageItineraryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlanVoyageItineraryResponse>> PlanItinerary(
        Guid voyageId,
        [FromBody] PlanVoyageItineraryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await planVoyageItineraryService.PlanAsync(
                voyageId,
                request,
                cancellationToken);

            return Created($"/api/voyages/{voyageId}/itinerary", response);
        }
        catch (PlanVoyageItineraryValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
        catch (RouteNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested route was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageStopPortNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested port was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageItineraryNotPlannableException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage cannot receive an itinerary from its current status.",
                Detail = exception.Message,
            });
        }
        catch (VoyageItineraryAlreadyPlannedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage already has an itinerary.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet("{voyageId:guid}/itinerary")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<VoyageItineraryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VoyageItineraryResponse>> GetItinerary(
        Guid voyageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getVoyageItineraryQuery.GetItineraryAsync(
                voyageId,
                cancellationToken);

            return Ok(response);
        }
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet("ports/{portId:guid}/berth-availability")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<BerthAvailabilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BerthAvailabilityResponse>> GetBerthAvailability(
        Guid portId,
        [FromQuery] BerthAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        try { return Ok(await getBerthAvailabilityQuery.GetAsync(portId, request, cancellationToken)); }
        catch (BerthAvailabilityValidationException exception) { return BadRequest(ToValidationProblem(exception.Errors)); }
        catch (PortNotFoundException exception) { return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "The requested port was not found.", Detail = exception.Message }); }
    }

    [HttpPut("{voyageId:guid}/stops/{stopId:guid}/berth-allocation")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<ReplaceBerthAllocationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReplaceBerthAllocationResponse>> ReplaceBerthAllocation(
        Guid voyageId, Guid stopId, [FromBody] ReplaceBerthAllocationRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await replaceBerthAllocationService.ReplaceAsync(voyageId, stopId, request, GetActingUserId(), cancellationToken)); }
        catch (ReplaceBerthAllocationValidationException exception) { return BadRequest(ToValidationProblem(exception.Errors)); }
        catch (VoyageNotFoundException exception) { return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "The requested voyage was not found.", Detail = exception.Message }); }
        catch (VoyageStopNotFoundException exception) { return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "The requested voyage stop was not found.", Detail = exception.Message }); }
        catch (BerthNotFoundException exception) { return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "The requested berth was not found.", Detail = exception.Message }); }
        catch (BerthPortMismatchException exception) { return Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "The berth belongs to a different port.", Detail = exception.Message }); }
        catch (BerthDimensionMismatchException exception) { return Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "The berth cannot accommodate the vessel.", Detail = exception.Message }); }
        catch (BerthOverlapException exception) { return Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "The berth is not available for the requested window.", Detail = exception.Message }); }
        catch (VoyageBerthAllocationNotAllowedException exception) { return Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "The voyage cannot be rescheduled from its current status.", Detail = exception.Message }); }
        catch (VoyageStopNotPlannedException exception) { return Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "The voyage stop cannot be rescheduled from its current status.", Detail = exception.Message }); }
    }

    [HttpGet("{voyageId:guid}/stops/{stopId:guid}/berth-allocation-history")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<BerthAllocationHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BerthAllocationHistoryResponse>> GetBerthAllocationHistory(
        Guid voyageId, Guid stopId, CancellationToken cancellationToken)
    {
        try { return Ok(await getBerthAllocationHistoryQuery.GetAsync(voyageId, stopId, cancellationToken)); }
        catch (VoyageStopNotFoundException exception) { return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "The requested voyage stop was not found.", Detail = exception.Message }); }
    }

    [HttpPost("{voyageId:guid}/stops/{stopId:guid}/berth")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<AssignBerthToStopResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssignBerthToStopResponse>> AssignBerth(
        Guid voyageId,
        Guid stopId,
        [FromBody] AssignBerthToStopRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await assignBerthService.AssignAsync(
                voyageId,
                stopId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (AssignBerthValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (BerthPortMismatchException exception)
        {
            return BadRequest(ToValidationProblem(new Dictionary<string, string[]>
            {
                [AssignBerthValidator.BerthIdField] = [exception.Message],
            }));
        }
        catch (VoyageStopNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage stop was not found.",
                Detail = exception.Message,
            });
        }
        catch (BerthNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested berth was not found.",
                Detail = exception.Message,
            });
        }
        catch (BerthOverlapException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The berth is not available for the requested window.",
                Detail = exception.Message,
            });
        }
        catch (VoyageStopNotPlannedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage stop cannot be changed from its current status.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{voyageId:guid}/stops/{stopId:guid}/arrival")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<RecordStopArrivalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordStopArrivalResponse>> RecordStopArrival(
        Guid voyageId,
        Guid stopId,
        [FromBody] RecordStopArrivalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await recordStopArrivalService.RecordArrivalAsync(
                voyageId,
                stopId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (RecordStopArrivalValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageStopNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage stop was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageNotExecutingException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage is not in progress.",
                Detail = exception.Message,
            });
        }
        catch (VoyageStopOutOfSequenceException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The previous voyage stop has not departed yet.",
                Detail = exception.Message,
            });
        }
        catch (VoyageStopNotPlannedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage stop cannot arrive from its current status.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{voyageId:guid}/stops/{stopId:guid}/departure")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<RecordStopDepartureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordStopDepartureResponse>> RecordStopDeparture(
        Guid voyageId,
        Guid stopId,
        [FromBody] RecordStopDepartureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await recordStopDepartureService.RecordDepartureAsync(
                voyageId,
                stopId,
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (RecordStopDepartureValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageStopNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage stop was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageNotExecutingException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage is not in progress.",
                Detail = exception.Message,
            });
        }
        catch (VoyageStopNotArrivedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage stop cannot depart from its current status.",
                Detail = exception.Message,
            });
        }
        catch (VoyageStopClearanceNotCompletedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The port-call clearance is not completed.",
                Detail = exception.Message,
            });
        }
        catch (PortCallClearanceConflictException)
        {
            return ClearanceConcurrentChange();
        }
    }

    [HttpGet("{voyageId:guid}/stops/{stopId:guid}/clearance")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<PortCallClearanceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortCallClearanceResponse>> GetPortCallClearance(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getPortCallClearanceQuery.GetAsync(voyageId, stopId, cancellationToken));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallClearanceNotFoundException exception)
        {
            return ClearanceNotFound(exception);
        }
    }

    [HttpPost("{voyageId:guid}/stops/{stopId:guid}/clearance/items")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<AddPortCallClearanceItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AddPortCallClearanceItemResponse>> AddPortCallClearanceItem(
        Guid voyageId,
        Guid stopId,
        [FromBody] AddPortCallClearanceItemRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await addPortCallClearanceItemService.AddAsync(
                voyageId,
                stopId,
                request,
                GetActingUserId(),
                cancellationToken);

            return Created(
                $"/api/voyages/{voyageId}/stops/{stopId}/clearance/items/{response.ItemId}",
                response);
        }
        catch (PortCallClearanceValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallClearanceNotFoundException exception)
        {
            return ClearanceNotFound(exception);
        }
        catch (VoyageStopAlreadyDepartedException exception)
        {
            return StopAlreadyDeparted(exception);
        }
        catch (PortCallClearanceAlreadyCompletedException exception)
        {
            return ClearanceCompleted(exception);
        }
        catch (PortCallClearanceConflictException)
        {
            return ClearanceConcurrentChange();
        }
    }

    [HttpPut("{voyageId:guid}/stops/{stopId:guid}/clearance/items/{itemId:guid}")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdatePortCallClearanceItem(
        Guid voyageId,
        Guid stopId,
        Guid itemId,
        [FromBody] UpdatePortCallClearanceItemRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await updatePortCallClearanceItemService.UpdateAsync(
                voyageId,
                stopId,
                itemId,
                request,
                GetActingUserId(),
                cancellationToken);

            return NoContent();
        }
        catch (PortCallClearanceValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (PortCallClearanceAlreadyCompletedException exception)
        {
            return ClearanceCompleted(exception);
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallClearanceNotFoundException exception)
        {
            return ClearanceNotFound(exception);
        }
        catch (PortCallClearanceItemNotFoundException exception)
        {
            return ClearanceItemNotFound(exception);
        }
        catch (VoyageStopAlreadyDepartedException exception)
        {
            return StopAlreadyDeparted(exception);
        }
        catch (PortCallClearanceItemNotPendingException exception)
        {
            return ClearanceItemNotPending(exception);
        }
        catch (PortCallClearanceConflictException)
        {
            return ClearanceConcurrentChange();
        }
    }

    [HttpPost("{voyageId:guid}/stops/{stopId:guid}/clearance/items/{itemId:guid}/clear")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<PortCallClearanceItemDecisionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortCallClearanceItemDecisionResponse>> ClearPortCallClearanceItem(
        Guid voyageId,
        Guid stopId,
        Guid itemId,
        [FromBody] ClearPortCallClearanceItemRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await clearPortCallClearanceItemService.ClearAsync(
                voyageId,
                stopId,
                itemId,
                request,
                GetActingUserId(),
                cancellationToken));
        }
        catch (PortCallClearanceValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallClearanceNotFoundException exception)
        {
            return ClearanceNotFound(exception);
        }
        catch (PortCallClearanceItemNotFoundException exception)
        {
            return ClearanceItemNotFound(exception);
        }
        catch (VoyageStopAlreadyDepartedException exception)
        {
            return StopAlreadyDeparted(exception);
        }
        catch (PortCallClearanceItemNotPendingException exception)
        {
            return ClearanceItemNotPending(exception);
        }
        catch (PortCallClearanceConflictException)
        {
            return ClearanceConcurrentChange();
        }
    }

    [HttpPost("{voyageId:guid}/stops/{stopId:guid}/clearance/items/{itemId:guid}/waive")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<PortCallClearanceItemDecisionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortCallClearanceItemDecisionResponse>> WaivePortCallClearanceItem(
        Guid voyageId,
        Guid stopId,
        Guid itemId,
        [FromBody] WaivePortCallClearanceItemRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await waivePortCallClearanceItemService.WaiveAsync(
                voyageId,
                stopId,
                itemId,
                request,
                GetActingUserId(),
                cancellationToken));
        }
        catch (PortCallClearanceValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallClearanceNotFoundException exception)
        {
            return ClearanceNotFound(exception);
        }
        catch (PortCallClearanceItemNotFoundException exception)
        {
            return ClearanceItemNotFound(exception);
        }
        catch (VoyageStopAlreadyDepartedException exception)
        {
            return StopAlreadyDeparted(exception);
        }
        catch (PortCallClearanceItemNotPendingException exception)
        {
            return ClearanceItemNotPending(exception);
        }
        catch (PortCallClearanceConflictException)
        {
            return ClearanceConcurrentChange();
        }
    }

    [HttpPost("{voyageId:guid}/stops/{stopId:guid}/clearance/complete")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<PortCallClearanceStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortCallClearanceStatusResponse>> CompletePortCallClearance(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await completePortCallClearanceService.CompleteAsync(
                voyageId,
                stopId,
                GetActingUserId(),
                cancellationToken));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallClearanceNotFoundException exception)
        {
            return ClearanceNotFound(exception);
        }
        catch (VoyageStopAlreadyDepartedException exception)
        {
            return StopAlreadyDeparted(exception);
        }
        catch (PortCallClearanceAlreadyCompletedException exception)
        {
            return ConflictProblem("The port-call clearance is already completed.", exception.Message);
        }
        catch (PortCallClearanceNotCompletableException exception)
        {
            return ConflictProblem("The port-call clearance is not ready to be completed.", exception.Message);
        }
        catch (PortCallClearanceConflictException)
        {
            return ClearanceConcurrentChange();
        }
    }

    [HttpPost("{voyageId:guid}/stops/{stopId:guid}/clearance/reopen")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<PortCallClearanceStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PortCallClearanceStatusResponse>> ReopenPortCallClearance(
        Guid voyageId,
        Guid stopId,
        [FromBody] ReopenPortCallClearanceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await reopenPortCallClearanceService.ReopenAsync(
                voyageId,
                stopId,
                request,
                GetActingUserId(),
                cancellationToken));
        }
        catch (PortCallClearanceValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return VoyageNotFound(exception);
        }
        catch (VoyageStopNotFoundException exception)
        {
            return VoyageStopNotFound(exception);
        }
        catch (PortCallClearanceNotFoundException exception)
        {
            return ClearanceNotFound(exception);
        }
        catch (VoyageStopAlreadyDepartedException exception)
        {
            return StopAlreadyDeparted(exception);
        }
        catch (PortCallClearanceNotCompletedException exception)
        {
            return ConflictProblem(
                "The port-call clearance cannot be reopened from its current status.",
                exception.Message);
        }
        catch (PortCallClearanceConflictException)
        {
            return ClearanceConcurrentChange();
        }
    }

    [HttpPost("{voyageId:guid}/delays")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<RecordDelayResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecordDelayResponse>> RecordDelay(
        Guid voyageId,
        [FromBody] RecordDelayRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await recordDelayService.RecordAsync(voyageId, request, cancellationToken);

            return Created($"/api/voyages/{voyageId}/delays/{response.DelayId}", response);
        }
        catch (RecordDelayValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageStopNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage stop was not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{voyageId:guid}/delays/{delayId:guid}/impact-preview")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<DelayImpactPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DelayImpactPreviewResponse>> PreviewDelayImpact(
        Guid voyageId,
        Guid delayId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await delayImpactPreviewService.PreviewAsync(
                voyageId,
                delayId,
                cancellationToken);

            return Ok(response);
        }
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
        catch (DelayNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested delay was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageStopNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage stop was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageNotRevisableException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage cannot be revised from its current status.",
                Detail = exception.Message,
            });
        }
        catch (DelayImpactAlreadyAppliedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The delay impact was already applied.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPut("{voyageId:guid}/schedule")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<VoyageScheduleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VoyageScheduleResponse>> AmendSchedule(
        Guid voyageId,
        [FromBody] AmendVoyageScheduleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await amendVoyageScheduleService.AmendAsync(
                voyageId,
                GetActingUserId(),
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (AmendVoyageScheduleValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
        catch (PortNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested port was not found.",
                Detail = exception.Message,
            });
        }
        catch (RouteNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested route was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageRouteEndpointMismatchException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The route does not connect the requested ports.",
                Detail = exception.Message,
            });
        }
        catch (VoyageScheduleNotAmendableException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage schedule cannot be changed from its current status.",
                Detail = exception.Message,
            });
        }
        catch (VoyageScheduleUnchangedException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage schedule is unchanged.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("{voyageId:guid}/cancel")]
    [Authorize(ManageVoyagesPolicy)]
    [ProducesResponseType<VoyageScheduleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VoyageScheduleResponse>> Cancel(
        Guid voyageId,
        [FromBody] CancelVoyageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await cancelVoyageService.CancelAsync(
                voyageId,
                GetActingUserId(),
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (CancelVoyageValidationException exception)
        {
            return BadRequest(ToValidationProblem(exception.Errors));
        }
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
        catch (VoyageScheduleNotAmendableException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The voyage cannot be cancelled from its current status.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet("{voyageId:guid}/schedule-changes")]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<VoyageScheduleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VoyageScheduleResponse>> GetScheduleChanges(
        Guid voyageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getVoyageScheduleChangesQuery.GetScheduleChangesAsync(
                voyageId,
                cancellationToken);

            return Ok(response);
        }
        catch (VoyageNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested voyage was not found.",
                Detail = exception.Message,
            });
        }
    }

    private Guid GetActingUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private NotFoundObjectResult VoyageNotFound(VoyageNotFoundException exception) =>
        NotFoundProblem("The requested voyage was not found.", exception.Message);

    private NotFoundObjectResult VoyageStopNotFound(VoyageStopNotFoundException exception) =>
        NotFoundProblem("The requested voyage stop was not found.", exception.Message);

    private NotFoundObjectResult ClearanceNotFound(PortCallClearanceNotFoundException exception) =>
        NotFoundProblem("The requested port-call clearance was not found.", exception.Message);

    private NotFoundObjectResult ClearanceItemNotFound(
        PortCallClearanceItemNotFoundException exception) =>
        NotFoundProblem("The requested clearance item was not found.", exception.Message);

    private ConflictObjectResult StopAlreadyDeparted(VoyageStopAlreadyDepartedException exception) =>
        ConflictProblem("The voyage stop has already departed.", exception.Message);

    private ConflictObjectResult ClearanceItemNotPending(
        PortCallClearanceItemNotPendingException exception) =>
        ConflictProblem("The clearance item is no longer pending.", exception.Message);

    private ConflictObjectResult ClearanceCompleted(
        PortCallClearanceAlreadyCompletedException exception) =>
        ConflictProblem(
            "The port-call clearance is completed. Reopen it before changing its items.",
            exception.Message);

    private ConflictObjectResult ClearanceConcurrentChange() =>
        ConflictProblem(
            "The port-call clearance was modified concurrently.",
            "The clearance was changed by another request. Reload and try again.");

    private NotFoundObjectResult NotFoundProblem(string title, string detail) =>
        NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = title,
            Detail = detail,
        });

    private ConflictObjectResult ConflictProblem(string title, string detail) =>
        Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = title,
            Detail = detail,
        });

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
