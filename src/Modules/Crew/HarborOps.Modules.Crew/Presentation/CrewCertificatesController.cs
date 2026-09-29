using System.Security.Claims;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/members/{crewMemberId:guid}/certificates")]
public sealed class CrewCertificatesController(
    RegisterCrewCertificateService registerCrewCertificateService,
    RenewCrewCertificateService renewCrewCertificateService,
    RevokeCrewCertificateService revokeCrewCertificateService,
    IGetCrewCertificateHistoryQuery getCrewCertificateHistoryQuery)
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

    [HttpPost("/api/crew/certificates/{certificateId:guid}/renew")]
    [ProducesResponseType<RenewCrewCertificateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RenewCrewCertificateResponse>> Renew(
        Guid certificateId,
        [FromBody] RenewCrewCertificateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await renewCrewCertificateService.RenewAsync(
                certificateId,
                GetActingUserId(),
                request,
                cancellationToken);

            return Created(
                $"/api/crew/members/{response.CrewMemberId}/certificates/{response.Id}",
                response);
        }
        catch (RenewCrewCertificateValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (CrewCertificateNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew certificate not found.", exception.Message));
        }
        catch (DuplicateCrewCertificateRenewalException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "This certificate already has a renewal successor.",
                exception.Message));
        }
        catch (DuplicateCrewCertificateLicenseException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "A certificate with this license number already exists for this crew member.",
                exception.Message));
        }
    }

    [HttpPost("/api/crew/certificates/{certificateId:guid}/revoke")]
    [ProducesResponseType<RevokeCrewCertificateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RevokeCrewCertificateResponse>> Revoke(
        Guid certificateId,
        [FromBody] RevokeCrewCertificateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await revokeCrewCertificateService.RevokeAsync(
                certificateId,
                GetActingUserId(),
                request,
                cancellationToken));
        }
        catch (RevokeCrewCertificateValidationException exception)
        {
            return BadRequest(ValidationProblem(exception.Errors));
        }
        catch (CrewCertificateNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew certificate not found.", exception.Message));
        }
        catch (CrewCertificateAlreadyRevokedException exception)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "This certificate is already revoked.",
                exception.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(Problem(
                StatusCodes.Status409Conflict,
                "Certificate was modified concurrently.",
                "The certificate was changed by another request. Reload and try again."));
        }
    }

    [HttpGet("/api/crew/certificates/{certificateId:guid}/history")]
    [ProducesResponseType<GetCrewCertificateHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetCrewCertificateHistoryResponse>> GetHistory(
        Guid certificateId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getCrewCertificateHistoryQuery.GetAsync(certificateId, cancellationToken));
        }
        catch (CrewCertificateNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Crew certificate not found.", exception.Message));
        }
    }

    private Guid GetActingUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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
