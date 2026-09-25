using System.Security.Claims;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Documents.Presentation;

[ApiController]
[Route("api/documents/requirements")]
public sealed class DocumentRequirementsController(
    IGetDocumentRequirementsQuery getDocumentRequirementsQuery,
    DocumentRequirementService documentRequirementService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetDocumentRequirementsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetDocumentRequirementsResponse>> List(
        [FromQuery] GetDocumentRequirementsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getDocumentRequirementsQuery.GetAsync(request, cancellationToken));
        }
        catch (GetDocumentRequirementsValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
    }

    [HttpGet("{requirementId:guid}")]
    [ProducesResponseType<DocumentRequirementDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentRequirementDetailsResponse>> GetDetails(
        Guid requirementId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getDocumentRequirementsQuery.GetByIdAsync(requirementId, cancellationToken));
        }
        catch (DocumentRequirementNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Document requirement not found.", exception.Message));
        }
    }

    [HttpPost]
    [ProducesResponseType<DocumentRequirementListItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentRequirementListItem>> Create(
        [FromBody] CreateDocumentRequirementRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await documentRequirementService.CreateAsync(actorUserId, request, cancellationToken);

            return Created($"/api/documents/requirements/{response.Id}", response);
        }
        catch (DocumentRequirementValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (DocumentTypeNotFoundException exception)
        {
            return BadRequest(ValidationProblem(new Dictionary<string, string[]>
            {
                [DocumentRequirementValidator.DocumentTypeIdField] = [exception.Message],
            }));
        }
        catch (DocumentRequirementVesselNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Vessel not found.", exception.Message));
        }
        catch (DuplicateDocumentRequirementException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "The vessel already has a requirement for this document type.",
                exception.Message));
        }
    }

    [HttpPut("{requirementId:guid}")]
    [ProducesResponseType<DocumentRequirementListItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentRequirementListItem>> Update(
        Guid requirementId,
        [FromBody] UpdateDocumentRequirementRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await documentRequirementService.UpdateAsync(requirementId, actorUserId, request, cancellationToken));
        }
        catch (DocumentRequirementValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (DocumentRequirementNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Document requirement not found.", exception.Message));
        }
        catch (DocumentRequirementConflictException exception)
        {
            return ConcurrentChange(exception.Message);
        }
    }

    [HttpPost("{requirementId:guid}/disable")]
    [ProducesResponseType<DocumentRequirementListItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentRequirementListItem>> Disable(
        Guid requirementId,
        [FromBody] DisableDocumentRequirementRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await documentRequirementService.DisableAsync(requirementId, actorUserId, request, cancellationToken));
        }
        catch (DocumentRequirementValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (DocumentRequirementNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Document requirement not found.", exception.Message));
        }
        catch (DocumentRequirementConflictException exception)
        {
            return ConcurrentChange(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(Problem(StatusCodes.Status409Conflict, "Document requirement cannot be disabled.", exception.Message));
        }
    }

    private ActionResult ConcurrentChange(string detail) =>
        Conflict(Problem(StatusCodes.Status409Conflict, "Document requirement was modified concurrently.", detail));

    private static HttpValidationProblemDetails ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        new(errors.ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };

    private static ProblemDetails Problem(int status, string title, string detail) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
    };
}
