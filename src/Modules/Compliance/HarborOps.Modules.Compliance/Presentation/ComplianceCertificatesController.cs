using System.Security.Claims;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Presentation;

[ApiController]
[Route("api/compliance/certificates")]
public sealed class ComplianceCertificatesController(
    RegisterComplianceCertificateService registerComplianceCertificateService,
    RenewComplianceCertificateService renewComplianceCertificateService,
    RevokeComplianceCertificateService revokeComplianceCertificateService,
    IGetComplianceCertificateDetailQuery getComplianceCertificateDetailQuery)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterComplianceCertificateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegisterComplianceCertificateResponse>> Register(
        [FromBody] RegisterComplianceCertificateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerComplianceCertificateService.RegisterAsync(request, cancellationToken);

            return Created($"/api/compliance/certificates/{response.Id}", response);
        }
        catch (RegisterComplianceCertificateValidationException exception)
        {
            return ValidationProblem(exception.Errors);
        }
    }

    [HttpGet("{certificateId:guid}")]
    [ProducesResponseType<GetComplianceCertificateDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetComplianceCertificateDetailResponse>> GetDetail(
        Guid certificateId,
        CancellationToken cancellationToken)
    {
        var response = await getComplianceCertificateDetailQuery.GetAsync(certificateId, cancellationToken);

        return response is null ? NotFound() : Ok(response);
    }

    [HttpPost("{certificateId:guid}/renewals")]
    [ProducesResponseType<RenewComplianceCertificateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RenewComplianceCertificateResponse>> Renew(
        Guid certificateId,
        [FromBody] RenewComplianceCertificateRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await renewComplianceCertificateService.RenewAsync(
                certificateId, actorUserId, request, cancellationToken);

            return Created($"/api/compliance/certificates/{response.Id}", response);
        }
        catch (RenewComplianceCertificateValidationException exception)
        {
            return ValidationProblem(exception.Errors);
        }
        catch (ComplianceCertificateNotFoundException exception)
        {
            return CertificateNotFound(exception);
        }
        catch (ComplianceCertificateConflictException exception)
        {
            return CertificateConflict(exception.Message);
        }
        catch (InvalidComplianceCertificateTransitionException exception)
        {
            return CertificateConflict(exception.Message);
        }
        catch (DbUpdateException)
        {
            return CertificateConflict("The certificate was renewed or changed concurrently. Reload it and try again.");
        }
    }

    [HttpPost("{certificateId:guid}/revoke")]
    [ProducesResponseType<RevokeComplianceCertificateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RevokeComplianceCertificateResponse>> Revoke(
        Guid certificateId,
        [FromBody] RevokeComplianceCertificateRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await revokeComplianceCertificateService.RevokeAsync(
                certificateId, actorUserId, request, cancellationToken));
        }
        catch (RevokeComplianceCertificateValidationException exception)
        {
            return ValidationProblem(exception.Errors);
        }
        catch (ComplianceCertificateNotFoundException exception)
        {
            return CertificateNotFound(exception);
        }
        catch (ComplianceCertificateConflictException exception)
        {
            return CertificateConflict(exception.Message);
        }
        catch (InvalidComplianceCertificateTransitionException exception)
        {
            return CertificateConflict(exception.Message);
        }
        catch (DbUpdateException)
        {
            return CertificateConflict("The certificate was changed concurrently. Reload it and try again.");
        }
    }

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        return BadRequest(new HttpValidationProblemDetails(
            errors.ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        });
    }

    private ActionResult CertificateNotFound(ComplianceCertificateNotFoundException exception)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Compliance certificate not found.",
            Detail = exception.Message,
        });
    }

    private ActionResult CertificateConflict(string detail)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "The certificate could not be changed.",
            Detail = detail,
        });
    }
}
