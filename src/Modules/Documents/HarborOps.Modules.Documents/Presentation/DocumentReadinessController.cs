using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Documents.Presentation;

[ApiController]
[Route("api/documents/readiness")]
public sealed class DocumentReadinessController(
    IGetDocumentReadinessQuery getDocumentReadinessQuery,
    IGetVesselReadinessOverviewQuery getVesselReadinessOverviewQuery,
    IAuthorizationService authorizationService)
    : ControllerBase
{
    private const string ManageCapability = "documents.manage";

    [HttpGet]
    [ProducesResponseType<GetDocumentReadinessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetDocumentReadinessResponse>> Get(
        [FromQuery] GetDocumentReadinessRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var restrictToReadOnlyVisible = !(await authorizationService.AuthorizeAsync(User, ManageCapability)).Succeeded;

            return Ok(await getDocumentReadinessQuery.GetAsync(request, restrictToReadOnlyVisible, cancellationToken));
        }
        catch (GetDocumentReadinessValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (DocumentRequirementVesselNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Vessel not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpGet("vessels")]
    [ProducesResponseType<GetVesselReadinessOverviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetVesselReadinessOverviewResponse>> GetVessels(
        [FromQuery] GetVesselReadinessOverviewRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getVesselReadinessOverviewQuery.GetAsync(request, cancellationToken));
        }
        catch (GetDocumentReadinessValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
    }

    private static HttpValidationProblemDetails ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        new(errors.ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };
}
