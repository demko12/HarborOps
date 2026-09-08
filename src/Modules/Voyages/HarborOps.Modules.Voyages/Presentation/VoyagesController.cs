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
    AmendVoyageScheduleService amendVoyageScheduleService,
    CancelVoyageService cancelVoyageService,
    IGetVoyageScheduleChangesQuery getVoyageScheduleChangesQuery,
    IGetBerthAvailabilityQuery getBerthAvailabilityQuery,
    ReplaceBerthAllocationService replaceBerthAllocationService,
    IGetBerthAllocationHistoryQuery getBerthAllocationHistoryQuery)
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
