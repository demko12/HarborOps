using System.Security.Claims;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    IAuthorizationService authorizationService,
    IOptions<DocumentFileValidationOptions> documentFileValidationOptions,
    IGetRetentionPoliciesQuery getRetentionPoliciesQuery,
    RetentionPolicyAdminService retentionPolicyAdminService,
    IGetRetentionQueueQuery getRetentionQueueQuery,
    DocumentRetentionService documentRetentionService,
    LegalHoldService legalHoldService,
    DispositionRequestService dispositionRequestService,
    IGetDocumentRetentionStateQuery getDocumentRetentionStateQuery,
    DocumentDistributionService documentDistributionService)
    : ControllerBase
{
    private const string FileField = "file";
    private const string ReadCapability = "documents.read";
    private const string ManageCapability = "documents.manage";
    private const string DisposeCapability = "documents.dispose";

    // GetDocumentByIdQuery relabels an Active document's Status as "Expired" once its
    // ExpiryDate has passed (display-only - the persisted Status column is unchanged),
    // so "Expired" is treated as visibility-equivalent to Active here.
    private static readonly string[] ReadOnlyVisibleStatuses = [Document.ActiveStatus, "Expired", Document.SupersededStatus];

    private async Task<bool> HasManageCapabilityAsync() =>
        (await authorizationService.AuthorizeAsync(User, ManageCapability)).Succeeded;

    private static bool IsVisibleToReadOnlyUsers(string status, string reviewStatus) =>
        string.Equals(reviewStatus, Document.ApprovedReviewStatus, StringComparison.Ordinal)
        && ReadOnlyVisibleStatuses.Contains(status, StringComparer.Ordinal);

    private async Task<bool> CanViewAsync(string status, string reviewStatus) =>
        await HasManageCapabilityAsync() || IsVisibleToReadOnlyUsers(status, reviewStatus);

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

    [HttpPost("/api/documents/{documentId:guid}/submit-for-review")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitForReview(Guid documentId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Unauthorized();
        }

        try
        {
            await documentLifecycleService.SubmitForReviewAsync(documentId, actorId, cancellationToken);

            return NoContent();
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
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document cannot be submitted for review.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("/api/documents/{documentId:guid}/review")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Review(Guid documentId, [FromBody] ReviewDocumentRequest request, CancellationToken cancellationToken)
    {
        var errors = ReviewDocumentValidator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(new HttpValidationProblemDetails(
                errors.ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var reviewerId))
        {
            return Unauthorized();
        }

        try
        {
            await documentLifecycleService.ReviewAsync(documentId, reviewerId, request.Decision!, request.Notes, cancellationToken);

            return NoContent();
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
        catch (DocumentLifecycleConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document review conflict.",
                Detail = exception.Message,
            });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document cannot be reviewed.",
                Detail = exception.Message,
            });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document was modified concurrently.",
                Detail = "The document was changed by another request. Reload and try again.",
            });
        }
    }

    [HttpPost("/api/documents/{documentId:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revoke(Guid documentId, [FromBody] RevokeDocumentRequest request, CancellationToken cancellationToken)
    {
        var errors = RevokeDocumentValidator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(new HttpValidationProblemDetails(
                errors.ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Unauthorized();
        }

        try
        {
            await documentLifecycleService.RevokeAsync(documentId, actorId, request.Reason!, cancellationToken);

            return NoContent();
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
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document cannot be revoked.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document was modified concurrently.",
                Detail = "The document was changed by another request. Reload and try again.",
            });
        }
    }

    [HttpGet("/api/documents/{documentId:guid}/review-history")]
    [ProducesResponseType<IReadOnlyList<DocumentReviewEventResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DocumentReviewEventResponse>>> ReviewHistory(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            // GetReviewHistoryAsync returns only the event log (no Status/ReviewStatus of
            // the document itself), so an extra GetByIdAsync call supplies what the
            // visibility check needs - same approach as DownloadFile above.
            var details = await getDocumentByIdQuery.GetByIdAsync(documentId, cancellationToken);

            if (!await CanViewAsync(details.Status, details.ReviewStatus))
            {
                return Forbid();
            }

            return Ok(await documentLifecycleService.GetReviewHistoryAsync(documentId, cancellationToken));
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

    private static ProblemDetails Problem(string detail) => new() { Status = StatusCodes.Status404NotFound, Title = "Document not found.", Detail = detail };
    private static HttpValidationProblemDetails Validation(IReadOnlyDictionary<string, string[]> errors, int status) => new(errors) { Status = status, Title = status == 409 ? "Document lifecycle conflict." : "One or more validation errors occurred." };

    [HttpPost("{documentId:guid}/distribution-issues")]
    [Authorize(ManageCapability)]
    public async Task<ActionResult<DocumentDistributionIssueResponse>> IssueDistribution(Guid documentId, [FromBody] IssueDocumentDistributionRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        try
        {
            var result = await documentDistributionService.IssueAsync(documentId, request, actorId, cancellationToken);
            return Created($"/api/documents/{documentId}/distribution-issues/{result.Id}", result);
        }
        catch (DocumentNotFoundException exception) { return NotFound(Problem(exception.Message)); }
        catch (DocumentDistributionValidationException exception) { return BadRequest(Validation(exception.Errors, StatusCodes.Status400BadRequest)); }
        catch (DocumentDistributionConflictException exception) { return Conflict(Validation(new Dictionary<string, string[]> { ["document"] = [exception.Message] }, StatusCodes.Status409Conflict)); }
    }

    [HttpGet("distribution-recipients")]
    [Authorize(ManageCapability)]
    public async Task<ActionResult<IReadOnlyList<DocumentDistributionRecipientIdentity>>> SearchDistributionRecipients([FromQuery] string? search, [FromQuery] int? take, [FromServices] IDocumentDistributionRecipientLookup recipientLookup, CancellationToken cancellationToken)
    {
        if (take is < 1 or > 100) return BadRequest(Validation(new Dictionary<string, string[]> { ["take"] = ["Take must be between 1 and 100."] }, StatusCodes.Status400BadRequest));
        return Ok(await recipientLookup.SearchAsync(search?.Trim(), take ?? 25, cancellationToken));
    }

    [HttpGet("{documentId:guid}/distribution-issues/{issueId:guid}")]
    [Authorize(ManageCapability)]
    public async Task<ActionResult<DocumentDistributionIssueResponse>> GetDistributionIssue(Guid documentId, Guid issueId, CancellationToken cancellationToken)
    {
        try { return Ok(await documentDistributionService.GetIssueAsync(documentId, issueId, cancellationToken)); }
        catch (DocumentDistributionIssueNotFoundException exception) { return NotFound(Problem(exception.Message)); }
    }

    [HttpGet("{documentId:guid}/distribution-issues")]
    [Authorize(ManageCapability)]
    public async Task<ActionResult<IReadOnlyList<DocumentDistributionIssueResponse>>> GetDistributionIssues(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            var exists = await getDocumentByIdQuery.GetByIdAsync(documentId, cancellationToken);
            return Ok(await documentDistributionService.GetIssuesAsync(exists.Id, cancellationToken));
        }
        catch (DocumentNotFoundException exception) { return NotFound(Problem(exception.Message)); }
    }

    [HttpPost("{documentId:guid}/distribution-issues/{issueId:guid}/cancel")]
    [Authorize(ManageCapability)]
    public async Task<IActionResult> CancelDistribution(Guid documentId, Guid issueId, [FromBody] CancelDocumentDistributionRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        try { await documentDistributionService.CancelAsync(documentId, issueId, request.Reason, actorId, cancellationToken); return NoContent(); }
        catch (DocumentDistributionIssueNotFoundException exception) { return NotFound(Problem(exception.Message)); }
        catch (DocumentDistributionValidationException exception) { return BadRequest(Validation(exception.Errors, StatusCodes.Status400BadRequest)); }
        catch (DocumentDistributionConflictException exception) { return Conflict(Validation(new Dictionary<string, string[]> { ["issue"] = [exception.Message] }, StatusCodes.Status409Conflict)); }
    }

    [HttpGet("acknowledgements")]
    [Authorize(ReadCapability)]
    public async Task<ActionResult<GetDocumentDistributionInboxResponse>> GetMyAcknowledgements([FromQuery] GetDocumentDistributionInboxRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        try { return Ok(await documentDistributionService.GetInboxAsync(actorId, request, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (DocumentDistributionValidationException exception) { return BadRequest(Validation(exception.Errors, StatusCodes.Status400BadRequest)); }
    }

    [HttpPost("distribution-issues/{issueId:guid}/acknowledge")]
    [Authorize(ReadCapability)]
    public async Task<ActionResult<DocumentDistributionRecipientResponse>> AcknowledgeDistribution(Guid issueId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        try { return Ok(await documentDistributionService.AcknowledgeAsync(issueId, actorId, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (DocumentDistributionIssueNotFoundException exception) { return NotFound(Problem(exception.Message)); }
        catch (DocumentNotFoundException exception) { return NotFound(Problem(exception.Message)); }
        catch (DocumentDistributionConflictException exception) { return Conflict(Validation(new Dictionary<string, string[]> { ["issue"] = [exception.Message] }, StatusCodes.Status409Conflict)); }
        catch (DbUpdateConcurrencyException) { return Conflict(Validation(new Dictionary<string, string[]> { ["issue"] = ["The document changed concurrently. Reload and try again."] }, StatusCodes.Status409Conflict)); }
    }

    [HttpGet("expiring")]
    [ProducesResponseType<GetExpiringDocumentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetExpiringDocumentsResponse>> GetExpiring(
        [FromQuery] GetExpiringDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var restrictToReadOnlyVisible = !await HasManageCapabilityAsync();
            var response = await getExpiringDocumentsQuery.GetExpiringDocumentsAsync(request, cancellationToken, restrictToReadOnlyVisible);

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
            var restrictToReadOnlyVisible = !await HasManageCapabilityAsync();
            var response = await getDocumentsQuery.GetDocumentsAsync(request, cancellationToken, restrictToReadOnlyVisible);

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

            if (!await CanViewAsync(response.Status, response.ReviewStatus))
            {
                return Forbid();
            }

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
            // A second lookup, in addition to GetFileAsync below: GetDocumentFileQuery's
            // DocumentFileContent carries only stream/filename/content-type/size, not
            // Status/ReviewStatus, so the visibility check needs this extra round trip
            // rather than a change to GetDocumentFileQuery (out of scope for this step).
            var details = await getDocumentByIdQuery.GetByIdAsync(documentId, cancellationToken);

            if (!await CanViewAsync(details.Status, details.ReviewStatus))
            {
                return Forbid();
            }

            // A third lookup, same rationale as the visibility check above: a Disposed
            // document's file must never be streamed by this endpoint, regardless of
            // capability - Forbid() reuses the same "not available" response the visibility
            // check already returns rather than inventing a new shape for this case.
            var retentionState = await getDocumentRetentionStateQuery.GetStateAsync(documentId, cancellationToken);
            if (string.Equals(retentionState, DocumentRetentionState.DisposedState, StringComparison.Ordinal))
            {
                return Forbid();
            }

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

    [HttpGet("retention-policies")]
    [Authorize(ReadCapability)]
    [ProducesResponseType<GetRetentionPoliciesResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GetRetentionPoliciesResponse>> GetRetentionPolicies(CancellationToken cancellationToken)
    {
        return Ok(await getRetentionPoliciesQuery.GetRetentionPoliciesAsync(cancellationToken));
    }

    [HttpPut("retention-policies/{documentTypeId:int}")]
    [Authorize(ManageCapability)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRetentionPolicy(int documentTypeId, [FromBody] UpdateRetentionPolicyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await retentionPolicyAdminService.UpdatePolicyAsync(documentTypeId, request, cancellationToken);

            return NoContent();
        }
        catch (DocumentTypeNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Document type not found.",
                Detail = exception.Message,
            });
        }
        catch (RetentionPolicyValidationException exception)
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

    [HttpGet("retention-queue")]
    [Authorize(ReadCapability)]
    [ProducesResponseType<GetRetentionQueueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetRetentionQueueResponse>> GetRetentionQueue(
        [FromQuery] GetRetentionQueueRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getRetentionQueueQuery.GetRetentionQueueAsync(request, cancellationToken));
        }
        catch (GetRetentionQueueValidationException exception)
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

    [HttpPost("/api/documents/{documentId:guid}/archive")]
    [Authorize(ManageCapability)]
    [ProducesResponseType<DocumentRetentionStateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentRetentionStateResponse>> Archive(
        Guid documentId,
        [FromBody] ArchiveDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await documentRetentionService.ArchiveAsync(documentId, actorId, cancellationToken));
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
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document cannot be archived.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document retention state was modified concurrently.",
                Detail = "The document's retention state was changed by another request. Reload and try again.",
            });
        }
    }

    [HttpPost("/api/documents/{documentId:guid}/restore")]
    [Authorize(ManageCapability)]
    [ProducesResponseType<DocumentRetentionStateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentRetentionStateResponse>> Restore(
        Guid documentId,
        [FromBody] RestoreDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await documentRetentionService.RestoreAsync(documentId, actorId, cancellationToken));
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
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document cannot be restored.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document retention state was modified concurrently.",
                Detail = "The document's retention state was changed by another request. Reload and try again.",
            });
        }
    }

    [HttpPost("/api/documents/{documentId:guid}/legal-holds")]
    [Authorize(ManageCapability)]
    [ProducesResponseType<PlaceLegalHoldResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlaceLegalHoldResponse>> PlaceLegalHold(
        Guid documentId,
        [FromBody] PlaceLegalHoldRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await legalHoldService.PlaceAsync(documentId, request, actorId, cancellationToken);

            return Created($"/api/documents/{documentId}/legal-holds/{response.Id}", response);
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
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("/api/documents/{documentId:guid}/legal-holds/{holdId:guid}/release")]
    [Authorize(ManageCapability)]
    [ProducesResponseType<ReleaseLegalHoldResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReleaseLegalHoldResponse>> ReleaseLegalHold(
        Guid documentId,
        Guid holdId,
        [FromBody] ReleaseLegalHoldRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await legalHoldService.ReleaseAsync(documentId, holdId, request, actorId, cancellationToken));
        }
        catch (LegalHoldNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Legal hold not found.",
                Detail = exception.Message,
            });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Detail = exception.Message,
            });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Legal hold cannot be released.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Legal hold was modified concurrently.",
                Detail = "The legal hold was changed by another request. Reload and try again.",
            });
        }
    }

    [HttpPost("/api/documents/{documentId:guid}/disposition-requests")]
    [Authorize(ManageCapability)]
    [ProducesResponseType<DispositionRequestResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DispositionRequestResponse>> CreateDispositionRequest(
        Guid documentId,
        [FromBody] CreateDispositionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await dispositionRequestService.CreateAsync(documentId, request, actorId, cancellationToken);

            return Created($"/api/documents/{documentId}/disposition-requests/{response.Id}", response);
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
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Detail = exception.Message,
            });
        }
        catch (RetentionConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document is not eligible for disposition.",
                Detail = exception.Message,
            });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document is not eligible for disposition.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Document retention state was modified concurrently.",
                Detail = "The document's retention state was changed by another request. Reload and try again.",
            });
        }
    }

    [HttpPost("/api/documents/{documentId:guid}/disposition-requests/{requestId:guid}/approve")]
    [Authorize(DisposeCapability)]
    [ProducesResponseType<DispositionRequestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DispositionRequestResponse>> ApproveDispositionRequest(
        Guid documentId,
        Guid requestId,
        [FromBody] ApproveDispositionRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await dispositionRequestService.ApproveAsync(requestId, request, actorId, cancellationToken));
        }
        catch (DispositionRequestNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Disposition request not found.",
                Detail = exception.Message,
            });
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
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Disposition request cannot be approved.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Disposition request was modified concurrently.",
                Detail = "The disposition request was changed by another request. Reload and try again.",
            });
        }
    }

    [HttpPost("/api/documents/{documentId:guid}/disposition-requests/{requestId:guid}/reject")]
    [Authorize(DisposeCapability)]
    [ProducesResponseType<DispositionRequestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DispositionRequestResponse>> RejectDispositionRequest(
        Guid documentId,
        Guid requestId,
        [FromBody] RejectDispositionRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await dispositionRequestService.RejectAsync(requestId, request, actorId, cancellationToken));
        }
        catch (DispositionRequestNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Disposition request not found.",
                Detail = exception.Message,
            });
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
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Detail = exception.Message,
            });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Disposition request cannot be rejected.",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Disposition request was modified concurrently.",
                Detail = "The disposition request was changed by another request. Reload and try again.",
            });
        }
    }
}
