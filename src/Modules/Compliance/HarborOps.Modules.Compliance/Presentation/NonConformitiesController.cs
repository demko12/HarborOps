using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Compliance.Presentation;

[ApiController]
[Route("api/compliance/non-conformities")]
public sealed class NonConformitiesController(
    RegisterNonConformityService registerNonConformityService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterNonConformityResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegisterNonConformityResponse>> Register(
        [FromBody] RegisterNonConformityRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerNonConformityService.RegisterAsync(request, cancellationToken);

            return Created($"/api/compliance/non-conformities/{response.Id}", response);
        }
        catch (RegisterNonConformityValidationException exception)
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
        catch (SurveyAuditNotFoundException exception)
        {
            return NotFound(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterNonConformityValidator.SurveyIdField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested survey audit was not found.",
            });
        }
        catch (PscInspectionNotFoundException exception)
        {
            return NotFound(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterNonConformityValidator.PscIdField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status404NotFound,
                Title = "The requested PSC inspection was not found.",
            });
        }
    }
}
