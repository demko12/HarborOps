using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/members/{crewMemberId:guid}/certificates")]
public sealed class CrewCertificatesController(RegisterCrewCertificateService registerCrewCertificateService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterCrewCertificateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterCrewCertificateResponse>> Register(
        Guid crewMemberId,
        [FromBody] RegisterCrewCertificateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerCrewCertificateService.RegisterAsync(
                request,
                crewMemberId,
                cancellationToken);

            return Created(
                $"/api/crew/members/{crewMemberId}/certificates/{response.Id}",
                response);
        }
        catch (RegisterCrewCertificateValidationException exception)
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
        catch (CrewMemberNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Crew member not found.",
                Detail = exception.Message,
            });
        }
        catch (DuplicateCrewCertificateLicenseException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A certificate with this license number already exists for this crew member.",
                Detail = exception.Message,
            });
        }
    }
}
