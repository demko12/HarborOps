using System.Security.Claims;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Presentation;

[ApiController]
[Route("api/compliance/psc-inspections")]
public sealed class PscInspectionsController(
    RegisterPscInspectionService registerPscInspectionService,
    PscInspectionWorkflowService workflowService,
    IGetPscInspectionDetailQuery detailQuery,
    IGetPscDeficienciesQuery deficienciesQuery,
    IGetPscDetentionHistoryQuery detentionHistoryQuery)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterPscInspectionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegisterPscInspectionResponse>> Register(
        [FromBody] RegisterPscInspectionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerPscInspectionService.RegisterAsync(request, cancellationToken);

            return Created($"/api/compliance/psc-inspections/{response.Id}", response);
        }
        catch (RegisterPscInspectionValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
    }

    [HttpGet("{pscInspectionId:int}")]
    public async Task<ActionResult<GetPscInspectionDetailResponse>> Get(int pscInspectionId, CancellationToken cancellationToken)
    {
        var response = await detailQuery.GetAsync(pscInspectionId, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("{pscInspectionId:int}/deficiencies")]
    public async Task<ActionResult<GetPscDeficienciesResponse>> GetDeficiencies(int pscInspectionId, CancellationToken cancellationToken)
    {
        var response = await deficienciesQuery.GetAsync(pscInspectionId, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpPost("{pscInspectionId:int}/deficiencies")]
    public async Task<ActionResult<RegisterPscDeficiencyResponse>> RegisterDeficiency(
        int pscInspectionId,
        [FromBody] RegisterPscDeficiencyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workflowService.RegisterDeficiencyAsync(pscInspectionId, request, cancellationToken);
            return Created($"/api/compliance/non-conformities/{response.NonConformityId}", response);
        }
        catch (PscInspectionWorkflowValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (PscInspectionNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{pscInspectionId:int}/detain")]
    public async Task<IActionResult> Detain(int pscInspectionId, [FromBody] DetainPscInspectionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await workflowService.DetainAsync(pscInspectionId, request, GetActingUserId(), cancellationToken);
            return NoContent();
        }
        catch (PscInspectionWorkflowValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (PscInspectionNotFoundException)
        {
            return NotFound();
        }
        catch (PscInspectionWorkflowConflictException exception)
        {
            return ConflictProblem(exception);
        }
        catch (DbUpdateException exception)
        {
            return ConflictProblem(new PscInspectionWorkflowConflictException("The detention decision conflicted with another change. Retry the operation.", exception));
        }
    }

    [HttpPost("{pscInspectionId:int}/release")]
    public async Task<IActionResult> Release(int pscInspectionId, [FromBody] ReleasePscInspectionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await workflowService.ReleaseAsync(pscInspectionId, request, GetActingUserId(), cancellationToken);
            return NoContent();
        }
        catch (PscInspectionWorkflowValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (PscInspectionNotFoundException)
        {
            return NotFound();
        }
        catch (PscInspectionWorkflowConflictException exception)
        {
            return ConflictProblem(exception);
        }
        catch (DbUpdateException exception)
        {
            return ConflictProblem(new PscInspectionWorkflowConflictException("The release decision conflicted with another change. Retry the operation.", exception));
        }
    }

    [HttpGet("{pscInspectionId:int}/detention-history")]
    public async Task<ActionResult<IReadOnlyList<PscDetentionDecisionItem>>> GetDetentionHistory(int pscInspectionId, CancellationToken cancellationToken)
    {
        var response = await detentionHistoryQuery.GetAsync(pscInspectionId, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    private Guid GetActingUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static HttpValidationProblemDetails ValidationProblem(IReadOnlyDictionary<string, string[]> errors) => new(
        errors.ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal))
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "One or more validation errors occurred.",
    };

    private static ObjectResult ConflictProblem(PscInspectionWorkflowConflictException exception) => new(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Title = "PSC inspection transition is not allowed.",
        Detail = exception.Message,
    })
    {
        StatusCode = StatusCodes.Status409Conflict,
    };
}
