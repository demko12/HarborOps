using System.Security.Claims;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/qualification-policies")]
public sealed class QualificationPoliciesController(
    IGetQualificationPoliciesQuery getQualificationPoliciesQuery,
    IGetQualificationPolicyDetailQuery getQualificationPolicyDetailQuery,
    CreateQualificationPolicyService createQualificationPolicyService,
    UpdateQualificationPolicyService updateQualificationPolicyService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetQualificationPoliciesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetQualificationPoliciesResponse>> List(
        [FromQuery] GetQualificationPoliciesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getQualificationPoliciesQuery.GetAsync(request, cancellationToken));
        }
        catch (GetQualificationPoliciesValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
    }

    [HttpGet("{policyId:guid}")]
    [ProducesResponseType<GetQualificationPolicyDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetQualificationPolicyDetailResponse>> GetDetail(
        Guid policyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getQualificationPolicyDetailQuery.GetAsync(policyId, cancellationToken));
        }
        catch (CrewQualificationPolicyNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Qualification policy not found.", exception.Message));
        }
    }

    [HttpPost]
    [ProducesResponseType<CreateQualificationPolicyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateQualificationPolicyResponse>> Create(
        [FromBody] CreateQualificationPolicyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await createQualificationPolicyService.CreateAsync(GetActingUserId(), request, cancellationToken);

            return Created($"/api/crew/qualification-policies/{response.Id}", response);
        }
        catch (CreateQualificationPolicyValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (CrewPositionNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew position not found.", exception.Message));
        }
        catch (DuplicateCrewQualificationPolicyException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "A qualification policy already exists for this rank.",
                exception.Message));
        }
    }

    [HttpPut("{policyId:guid}")]
    [ProducesResponseType<UpdateQualificationPolicyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdateQualificationPolicyResponse>> Update(
        Guid policyId,
        [FromBody] UpdateQualificationPolicyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await updateQualificationPolicyService.UpdateAsync(policyId, GetActingUserId(), request, cancellationToken));
        }
        catch (UpdateQualificationPolicyValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (CrewQualificationPolicyNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Qualification policy not found.", exception.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }
    }

    private Guid GetActingUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private ActionResult ConcurrentChange()
    {
        return Conflict(Problem(
            StatusCodes.Status409Conflict,
            "Qualification policy was modified concurrently.",
            "The policy was changed by another request. Reload and try again."));
    }

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
