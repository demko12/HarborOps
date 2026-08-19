using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Compliance.Presentation;

[ApiController]
[Route("api/compliance/insurance-policies")]
public sealed class InsurancePoliciesController(InsurancePolicyService insurancePolicyService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterInsurancePolicyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterInsurancePolicyResponse>> Register(
        RegisterInsurancePolicyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await insurancePolicyService.RegisterAsync(request, cancellationToken);
            return Created($"/api/compliance/insurance-policies/{response.Id}", response);
        }
        catch (InsurancePolicyValidationException exception)
        {
            return Validation(exception);
        }
        catch (DuplicateInsurancePolicyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Duplicate insurance policy",
                Detail = "A policy with this insurer and policy number already exists.",
            });
        }
    }

    [HttpGet]
    [ProducesResponseType<GetInsurancePoliciesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetInsurancePoliciesResponse>> Get(
        [FromQuery] GetInsurancePoliciesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await insurancePolicyService.GetAsync(request, cancellationToken));
        }
        catch (InsurancePolicyValidationException exception)
        {
            return Validation(exception);
        }
    }

    private static BadRequestObjectResult Validation(InsurancePolicyValidationException exception) =>
        new(new HttpValidationProblemDetails(exception.Errors.ToDictionary(
            error => error.Key,
            error => error.Value,
            StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        });
}
