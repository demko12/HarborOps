using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Cargo.Presentation;

[ApiController]
[Route("api/cargo/operations/{operationId:guid}")]
public sealed class CargoOperationExecutionController(
    ExecuteCargoOperationService executeCargoOperationService)
    : ControllerBase
{
    private const string ManageCargoPolicy = "cargo.manage";

    [HttpPost("start")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<CargoOperationTransitionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargoOperationTransitionResponse>> Start(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await executeCargoOperationService.StartAsync(
                operationId,
                cancellationToken);

            return Ok(response);
        }
        catch (CargoOperationNotFoundException exception)
        {
            return OperationNotFound(exception);
        }
        catch (CargoOperationBookingPhaseException exception)
        {
            return ConflictProblem(
                "Cargo booking is not in the right phase for this operation.",
                exception.Message);
        }
        catch (CargoOperationAlreadyInProgressException exception)
        {
            return ConflictProblem(
                "Cargo booking already has an operation in progress.",
                exception.Message);
        }
        catch (CargoManifestEmptyException exception)
        {
            return ConflictProblem("Cargo manifest is empty.", exception.Message);
        }
        catch (CargoManifestNotReadyException exception)
        {
            return ConflictProblem(
                "Cargo manifest is not ready for this operation.",
                exception.Message);
        }
        catch (CargoContainersNotReleasedException exception)
        {
            return ConflictProblem(
                "Containers are not released for loading.",
                exception.Message);
        }
        catch (CargoSafetyApprovalRequiredException exception)
        {
            return ConflictProblem(
                exception.ReviewStatus is null
                    ? "Cargo safety information is required before loading."
                    : "Cargo safety declaration is not approved.",
                exception.Message);
        }
        catch (CargoOperationTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
    }

    [HttpPost("complete")]
    [Authorize(ManageCargoPolicy)]
    [ProducesResponseType<CargoOperationTransitionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargoOperationTransitionResponse>> Complete(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await executeCargoOperationService.CompleteAsync(
                operationId,
                cancellationToken);

            return Ok(response);
        }
        catch (CargoOperationNotFoundException exception)
        {
            return OperationNotFound(exception);
        }
        catch (CargoOperationTransitionException exception)
        {
            return TransitionNotAllowed(exception);
        }
    }

    private ActionResult OperationNotFound(CargoOperationNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Cargo operation not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult TransitionNotAllowed(CargoOperationTransitionException exception)
    {
        return ConflictProblem("Cargo operation transition is not allowed.", exception.Message);
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
