using System.Security.Claims;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Cargo.Presentation;

[ApiController]
[Route("api/cargo/bookings/{bookingId:guid}")]
public sealed class CargoBookingExecutionController(
    GetCargoContainersQuery getCargoContainersQuery,
    AddCargoContainerService addCargoContainerService,
    ContainerManifestImportService containerManifestImportService,
    UpdateCargoContainerService updateCargoContainerService,
    GetCargoOperationsQuery getCargoOperationsQuery,
    PlanCargoOperationService planCargoOperationService)
    : ControllerBase
{
    private const string ReadCargoPolicy = "cargo.read";

    [HttpGet("containers")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<GetCargoContainersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetCargoContainersResponse>> GetContainers(
        Guid bookingId,
        CancellationToken cancellationToken,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = GetCargoContainersQuery.DefaultPageSize)
    {
        try
        {
            var response = await getCargoContainersQuery.GetContainersAsync(
                bookingId,
                pageNumber,
                pageSize,
                cancellationToken);

            return Ok(response);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (CargoContainerPageValidationException)
        {
            return BadRequest(new ProblemDetails { Title = "Page number must be positive and page size must be between 1 and 15." });
        }
    }

    [HttpPost("containers")]
    [Authorize("cargo.manage")]
    [ProducesResponseType<AddCargoContainerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AddCargoContainerResponse>> AddContainer(
        Guid bookingId,
        [FromBody] AddCargoContainerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await addCargoContainerService.AddAsync(
                bookingId,
                request,
                cancellationToken);

            return Created(
                $"/api/cargo/bookings/{bookingId}/containers/{response.Id}",
                response);
        }
        catch (AddCargoContainerValidationException exception)
        {
            return ValidationFailed(exception.Errors);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (CargoBookingCompletedException exception)
        {
            return BookingCompleted(exception);
        }
        catch (DuplicateContainerNumberException exception)
        {
            return Conflict(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [AddCargoContainerValidator.ContainerNumberField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A container with this number already exists.",
            });
        }
    }

    [HttpPost("containers/import/preview")]
    [Authorize("cargo.manage")]
    public async Task<ActionResult<ContainerManifestPreviewResponse>> PreviewImport(Guid bookingId, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        try
        {
            if (file is null || file.Length == 0 || file.Length > ContainerManifestImportService.MaxFileSizeBytes) return BadRequest(new ProblemDetails { Title = "Upload a non-empty .xlsx or .xml file smaller than 5 MB." });
            await using var stream = file.OpenReadStream();
            return Ok(await containerManifestImportService.PreviewAsync(bookingId, stream, file.FileName, cancellationToken));
        }
        catch (CargoBookingNotFoundException exception) { return BookingNotFound(exception); }
        catch (ContainerManifestFileException exception) { return BadRequest(new ProblemDetails { Title = "Invalid manifest file.", Detail = exception.Message }); }
    }

    [HttpPost("containers/import")]
    [Authorize("cargo.manage")]
    public async Task<ActionResult<ContainerManifestImportResponse>> Import(Guid bookingId, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        try
        {
            if (file is null || file.Length == 0 || file.Length > ContainerManifestImportService.MaxFileSizeBytes) return BadRequest(new ProblemDetails { Title = "Upload a non-empty .xlsx or .xml file smaller than 5 MB." });
            await using var stream = file.OpenReadStream();
            return Ok(await containerManifestImportService.ImportAsync(bookingId, stream, file.FileName, cancellationToken));
        }
        catch (CargoBookingNotFoundException exception) { return BookingNotFound(exception); }
        catch (CargoBookingCompletedException exception) { return BookingCompleted(exception); }
        catch (ContainerManifestImportValidationException exception) { return BadRequest(new ContainerManifestPreviewResponse(exception.Rows.Count, exception.Rows.Count(row => row.Errors.Count == 0), exception.Rows.Count(row => row.Errors.Count > 0), exception.Rows)); }
        catch (ContainerManifestFileException exception) { return BadRequest(new ProblemDetails { Title = "Invalid manifest file.", Detail = exception.Message }); }
        catch (DuplicateContainerNumberException exception) { return Conflict(new ProblemDetails { Title = "A container with this number already exists.", Detail = exception.Message }); }
    }

    [HttpPut("containers/{containerId:guid}")]
    [Authorize("cargo.manage")]
    public async Task<ActionResult<UpdateCargoContainerResponse>> UpdateContainer(Guid bookingId, Guid containerId, [FromBody] UpdateCargoContainerRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await updateCargoContainerService.UpdateAsync(bookingId, containerId, request, cancellationToken)); }
        catch (AddCargoContainerValidationException exception) { return ValidationFailed(exception.Errors); }
        catch (CargoBookingNotFoundException exception) { return BookingNotFound(exception); }
        catch (CargoContainerNotFoundException exception) { return NotFound(new ProblemDetails { Title = "Container not found.", Detail = exception.Message }); }
        catch (CargoBookingCompletedException exception) { return BookingCompleted(exception); }
        catch (CargoContainerNotEditableException exception) { return Conflict(new ProblemDetails { Title = "Container can no longer be edited.", Detail = exception.Message }); }
        catch (DuplicateContainerNumberException exception) { return Conflict(new ProblemDetails { Title = "A container with this number already exists.", Detail = exception.Message }); }
    }

    [HttpGet("operations")]
    [Authorize(ReadCargoPolicy)]
    [ProducesResponseType<GetCargoOperationsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetCargoOperationsResponse>> GetOperations(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getCargoOperationsQuery.GetOperationsAsync(
                bookingId,
                cancellationToken);

            return Ok(response);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
    }

    [HttpPost("operations")]
    [ProducesResponseType<PlanCargoOperationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlanCargoOperationResponse>> PlanOperation(
        Guid bookingId,
        [FromBody] PlanCargoOperationRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var responsibleUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await planCargoOperationService.PlanAsync(
                bookingId,
                responsibleUserId,
                request,
                cancellationToken);

            return Created(
                $"/api/cargo/bookings/{bookingId}/operations/{response.Id}",
                response);
        }
        catch (PlanCargoOperationValidationException exception)
        {
            return ValidationFailed(exception.Errors);
        }
        catch (CargoBookingNotFoundException exception)
        {
            return BookingNotFound(exception);
        }
        catch (CargoBookingCompletedException exception)
        {
            return BookingCompleted(exception);
        }
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

    private ActionResult BookingNotFound(CargoBookingNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Cargo booking not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult BookingCompleted(CargoBookingCompletedException exception)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Cargo booking is completed.",
            Detail = exception.Message,
        });
    }
}
