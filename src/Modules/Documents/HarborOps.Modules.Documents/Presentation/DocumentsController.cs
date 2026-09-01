using System.Security.Claims;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace HarborOps.Modules.Documents.Presentation;

[ApiController]
[Route("api/documents")]
public sealed class DocumentsController(
    RegisterDocumentService registerDocumentService,
    RegisterDocumentTypeService registerDocumentTypeService,
    IGetExpiringDocumentsQuery getExpiringDocumentsQuery,
    IGetDocumentTypesQuery getDocumentTypesQuery,
    IGetDocumentsQuery getDocumentsQuery,
    IGetDocumentByIdQuery getDocumentByIdQuery,
    IUploadDocumentFileService uploadDocumentFileService,
    IReplaceDocumentFileService replaceDocumentFileService,
    IGetDocumentFileQuery getDocumentFileQuery,
    DocumentLifecycleService documentLifecycleService,
    IOptions<DocumentFileValidationOptions> documentFileValidationOptions)
    : ControllerBase
{
    private const string FileField = "file";

    [HttpPut("{documentId:guid}/metadata")]
    public async Task<IActionResult> CorrectMetadata(Guid documentId, [FromBody] CorrectDocumentMetadataRequest request, CancellationToken cancellationToken)
    {
        try { await documentLifecycleService.CorrectMetadataAsync(documentId, request, cancellationToken); return NoContent(); }
        catch (DocumentNotFoundException exception) { return NotFound(Problem(exception.Message)); }
        catch (DocumentTypeNotFoundException exception) { return NotFound(Problem(exception.Message)); }
        catch (DocumentLifecycleValidationException exception) { return BadRequest(Validation(exception.Errors, StatusCodes.Status400BadRequest)); }
        catch (DocumentLifecycleConflictException exception) { return Conflict(Validation(new Dictionary<string, string[]> { ["number"] = [exception.Message] }, StatusCodes.Status409Conflict)); }
    }

    [HttpPost("{documentId:guid}/renewals")]
    public async Task<ActionResult<object>> Renew(Guid documentId, [FromBody] RenewDocumentRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        try
        {
            var successorId = await documentLifecycleService.RenewAsync(documentId, request, actorId, cancellationToken);
            return Created($"/api/documents/{successorId}", new { documentId = successorId });
        }
        catch (DocumentNotFoundException exception) { return NotFound(Problem(exception.Message)); }
        catch (DocumentTypeNotFoundException exception) { return NotFound(Problem(exception.Message)); }
        catch (DocumentLifecycleValidationException exception) { return Validation(exception.Errors, StatusCodes.Status400BadRequest); }
        catch (DocumentLifecycleConflictException exception) { return Validation(new Dictionary<string, string[]> { ["document"] = [exception.Message] }, StatusCodes.Status409Conflict); }
    }

    [HttpGet("{documentId:guid}/renewal-history")]
    public async Task<ActionResult<IReadOnlyList<DocumentRenewalHistoryItem>>> RenewalHistory(Guid documentId, CancellationToken cancellationToken)
    {
        try { return Ok(await documentLifecycleService.GetHistoryAsync(documentId, cancellationToken)); }
        catch (DocumentNotFoundException exception) { return NotFound(Problem(exception.Message)); }
    }

    private static ProblemDetails Problem(string detail) => new() { Status = StatusCodes.Status404NotFound, Title = "Document not found.", Detail = detail };
    private static HttpValidationProblemDetails Validation(IReadOnlyDictionary<string, string[]> errors, int status) => new(errors) { Status = status, Title = status == 409 ? "Document lifecycle conflict." : "One or more validation errors occurred." };

    [HttpGet("expiring")]
    [ProducesResponseType<GetExpiringDocumentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetExpiringDocumentsResponse>> GetExpiring(
        [FromQuery] GetExpiringDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getExpiringDocumentsQuery.GetExpiringDocumentsAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetExpiringDocumentsValidationException exception)
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

    [HttpGet("types")]
    [ProducesResponseType<GetDocumentTypesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetDocumentTypesResponse>> GetDocumentTypes(
        [FromQuery] GetDocumentTypesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getDocumentTypesQuery.GetDocumentTypesAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetDocumentTypesValidationException exception)
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

    [HttpGet]
    [ProducesResponseType<GetDocumentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetDocumentsResponse>> Get(
        [FromQuery] GetDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getDocumentsQuery.GetDocumentsAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (GetDocumentsValidationException exception)
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

    [HttpGet("/api/documents/{documentId:guid}")]
    [ProducesResponseType<DocumentDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentDetailsResponse>> GetById(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await getDocumentByIdQuery.GetByIdAsync(documentId, cancellationToken);

            return Ok(response);
        }
        catch (DocumentNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Document not found.",
                Detail = exception.Message,
            });
        }
    }

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

    [HttpPost("types")]
    [ProducesResponseType<RegisterDocumentTypeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterDocumentTypeResponse>> RegisterDocumentType(
        [FromBody] RegisterDocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerDocumentTypeService.RegisterAsync(request, cancellationToken);

            return Created($"/api/documents/types/{response.Id}", response);
        }
        catch (RegisterDocumentTypeValidationException exception)
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
        catch (DuplicateDocumentTypeNameException exception)
        {
            return Conflict(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [RegisterDocumentTypeValidator.NameField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A document type with this name already exists.",
            });
        }
    }

    [HttpPost("/api/documents/{documentId:guid}/file")]
    [ProducesResponseType<DocumentFileResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DocumentFileResponse>> UploadFile(
        Guid documentId,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (Request.ContentLength is { } contentLength &&
            contentLength > documentFileValidationOptions.Value.MaxSizeBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "File is too large.",
                Detail = $"The request body exceeds the maximum allowed size of " +
                    $"{documentFileValidationOptions.Value.MaxSizeBytes} bytes.",
            });
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [FileField] = ["A non-empty file is required."],
                })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uploadedBy))
        {
            return Unauthorized();
        }

        try
        {
            await using var content = file.OpenReadStream();
            var request = new UploadDocumentFileRequest(
                documentId,
                file.FileName,
                file.ContentType,
                file.Length,
                content,
                uploadedBy);

            var response = await uploadDocumentFileService.UploadAsync(request, cancellationToken);

            return CreatedAtAction(nameof(GetById), new { documentId }, response);
        }
        catch (DocumentNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Document not found.",
                Detail = exception.Message,
            });
        }
        catch (DocumentAlreadyHasFileException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document already has an attached file.",
                Detail = exception.Message,
            });
        }
        catch (FileTooLargeException exception)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "File is too large.",
                Detail = exception.Message,
            });
        }
        catch (UploadDocumentFileValidationException exception)
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
        catch (DocumentFileStorageException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An error occurred while processing the file.",
            });
        }
    }

    [HttpPut("/api/documents/{documentId:guid}/file")]
    [ProducesResponseType<DocumentFileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DocumentFileResponse>> ReplaceFile(
        Guid documentId,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (Request.ContentLength is { } contentLength &&
            contentLength > documentFileValidationOptions.Value.MaxSizeBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "File is too large.",
                Detail = $"The request body exceeds the maximum allowed size of " +
                    $"{documentFileValidationOptions.Value.MaxSizeBytes} bytes.",
            });
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [FileField] = ["A non-empty file is required."],
                })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uploadedBy))
        {
            return Unauthorized();
        }

        try
        {
            await using var content = file.OpenReadStream();
            var request = new UploadDocumentFileRequest(
                documentId,
                file.FileName,
                file.ContentType,
                file.Length,
                content,
                uploadedBy);

            var response = await replaceDocumentFileService.ReplaceAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (DocumentNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Document not found.",
                Detail = exception.Message,
            });
        }
        catch (DocumentFileNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Document does not have an attached file.",
                Detail = exception.Message,
            });
        }
        catch (FileTooLargeException exception)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "File is too large.",
                Detail = exception.Message,
            });
        }
        catch (UploadDocumentFileValidationException exception)
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
        catch (DocumentFileStorageException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An error occurred while processing the file.",
            });
        }
    }

    [HttpGet("/api/documents/{documentId:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadFile(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await getDocumentFileQuery.GetFileAsync(documentId, cancellationToken);

            var contentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = file.FileName,
            };
            Response.Headers.Append("Content-Disposition", contentDisposition.ToString());
            Response.Headers.Append("X-Content-Type-Options", "nosniff");

            return new FileStreamResult(file.Content, file.ContentType);
        }
        catch (DocumentNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Document not found.",
                Detail = exception.Message,
            });
        }
        catch (DocumentFileNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Document does not have an attached file.",
                Detail = exception.Message,
            });
        }
        catch (DocumentFileStorageException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An error occurred while processing the file.",
            });
        }
    }
}
