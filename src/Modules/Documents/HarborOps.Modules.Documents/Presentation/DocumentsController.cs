using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Documents.Presentation;

[ApiController]
[Route("api/documents")]
public sealed class DocumentsController(RegisterDocumentService registerDocumentService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterDocumentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterDocumentResponse>> Register(
        [FromBody] RegisterDocumentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerDocumentService.RegisterAsync(request, cancellationToken);

            return Created($"/api/documents/{response.Id}", response);
        }
        catch (RegisterDocumentValidationException exception)
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
        catch (DocumentTypeNotFoundException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterDocumentValidator.DocumentTypeIdField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Referenced document type does not exist.",
            });
        }
        catch (DuplicateDocumentNumberException exception)
        {
            return Conflict(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterDocumentValidator.NumberField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A document with this number already exists for this vessel.",
            });
        }
    }
}
