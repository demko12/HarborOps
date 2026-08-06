using System.Runtime.InteropServices;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Compliance.Presentation;

[ApiController]
[Route("api/compliance/certificates")]
public sealed class ComplianceCertificatesController(
    RegisterComplianceCertificateService registerComplianceCertificateService)
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
    }
}
