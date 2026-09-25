namespace HarborOps.Web.Api.Documents;

internal sealed record GetExpiringDocumentsQuery(
    DateOnly? From,
    DateOnly? To,
    Guid? VesselId,
    int? PageNumber,
    int? PageSize,
    string? ReviewStatus = null);

internal sealed record ExpiringDocumentListItem(
    Guid DocumentId,
    Guid VesselId,
    string Number,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    string IssuingAuthority,
    string Status,
    string ReviewStatus,
    string DocumentTypeName,
    int DaysUntilExpiry);

internal sealed record ExpiringDocumentsPage(
    IReadOnlyList<ExpiringDocumentListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record GetDocumentsQuery(Guid? VesselId, string? Search, int? PageNumber, int? PageSize);

internal sealed record DocumentListItem(
    Guid DocumentId,
    Guid VesselId,
    string Number,
    string DocumentTypeName,
    string Status,
    DateOnly ExpiryDate);

internal sealed record DocumentsPage(
    IReadOnlyList<DocumentListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record RegisterDocumentRequest
{
    public Guid? VesselId { get; init; }

    public int? DocumentTypeId { get; init; }

    public string? Number { get; init; }

    public DateOnly? IssueDate { get; init; }

    public DateOnly? ExpiryDate { get; init; }

    public string? IssuingAuthority { get; init; }

    public string? Status { get; init; }
}

internal sealed record RegisterDocumentResult(Guid Id);

internal sealed record GetDocumentTypesQuery(string? Search, int? PageSize);

internal sealed record DocumentTypeListItem(
    int DocumentTypeId,
    string Name,
    int MandatoryValidityPeriodDays,
    string IssuingAuthority,
    bool RequiresSurvey);

internal sealed record DocumentTypesPage(
    IReadOnlyList<DocumentTypeListItem> Items,
    int PageSize,
    int TotalCount);

internal sealed record RegisterDocumentTypeRequest
{
    public string? Name { get; init; }

    public int? MandatoryValidityPeriodDays { get; init; }

    public string? IssuingAuthority { get; init; }

    public bool? RequiresSurvey { get; init; }
}

internal sealed record RegisterDocumentTypeResult(int Id);

internal sealed record DocumentDetailsDto(
    Guid Id,
    Guid VesselId,
    int DocumentTypeId,
    string DocumentTypeName,
    string Number,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    string IssuingAuthority,
    string Status,
    string ReviewStatus,
    bool HasFile,
    string? FileName,
    string? ContentType,
    long? SizeBytes,
    Guid? UploadedBy,
    DateTime? UploadedAtUtc);

internal sealed record DocumentFileResponseDto(
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploadedBy,
    DateTime UploadedAtUtc);

internal sealed record DocumentMetadataRequest(int? DocumentTypeId, string? Number, DateOnly? IssueDate, DateOnly? ExpiryDate, string? IssuingAuthority);
internal sealed record DocumentRenewalHistoryItemDto(Guid DocumentId, Guid? PredecessorDocumentId, string Number, string Status, DateOnly ExpiryDate, DateTime? SupersededAtUtc, Guid? SupersededByUserId);

internal sealed record ReviewDocumentRequest(string? Decision, string? Notes);
internal sealed record RevokeDocumentRequest(string? Reason);
internal sealed record DocumentReviewEventResponseDto(Guid Id, Guid DocumentId, string Action, Guid ActorUserId, DateTimeOffset OccurredAtUtc, string? Notes);

internal sealed record RetentionPolicyRowDto(int DocumentTypeId, string DocumentTypeName, int RetentionPeriodDays);
internal sealed record RetentionPoliciesResultDto(IReadOnlyList<RetentionPolicyRowDto> Items);
internal sealed record UpdateRetentionPolicyRequest(int? RetentionPeriodDays);

internal sealed record GetRetentionQueueQuery(string? State, DateOnly? DueBefore, int? PageNumber, int? PageSize);
internal sealed record RetentionQueueActiveHoldDto(Guid Id, string Reason, DateTime PlacedAtUtc);
internal sealed record RetentionQueuePendingDispositionDto(Guid Id, Guid RequestedByUserId, DateTime RequestedAtUtc);
internal sealed record RetentionQueueRowDto(
    Guid DocumentId,
    int DocumentTypeId,
    DateTime LifecycleTriggerAtUtc,
    DateTime RetentionDueAtUtc,
    string RetentionState,
    IReadOnlyList<RetentionQueueActiveHoldDto> ActiveHolds,
    RetentionQueuePendingDispositionDto? PendingDisposition);
internal sealed record RetentionQueuePageDto(
    IReadOnlyList<RetentionQueueRowDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record ArchiveDocumentRequest(string? Reason);
internal sealed record RestoreDocumentRequest(string? Reason);
internal sealed record DocumentRetentionStateResultDto(Guid DocumentId, string State);

internal sealed record PlaceLegalHoldRequest(string? Reason, string? Reference);
internal sealed record PlaceLegalHoldResultDto(Guid Id);
internal sealed record ReleaseLegalHoldRequest(string? Reason);
internal sealed record ReleaseLegalHoldResultDto(Guid Id, bool IsActive);

internal sealed record CreateDispositionRequest(string? Reason);
internal sealed record ApproveDispositionRequestRequest(string? DecisionReason);
internal sealed record RejectDispositionRequestRequest(string? DecisionReason);
internal sealed record DispositionRequestResultDto(Guid Id, string Status);

internal sealed record DocumentDistributionInboxQuery(string? State, bool? Overdue, int? PageNumber, int? PageSize);
internal sealed record DocumentDistributionInboxItemDto(Guid IssueId, Guid DocumentId, string DocumentNumber, string DocumentType, DateOnly DocumentExpiryDate, DateOnly DueDate, string? Instruction, string State, bool IsOverdue, DateTime IssuedAtUtc, DateTime? AcknowledgedAtUtc, string? StateReason);
internal sealed record DocumentDistributionInboxPageDto(IReadOnlyList<DocumentDistributionInboxItemDto> Items, int PageNumber, int PageSize, int TotalCount);
internal sealed record DocumentDistributionRecipientDto(Guid RecipientUserId, string State, bool IsOverdue, DateTime? AcknowledgedAtUtc, DateTime? StateChangedAtUtc, string? StateReason);
internal sealed record DocumentDistributionIssueDto(Guid Id, Guid DocumentId, string DocumentNumber, string DocumentType, DateOnly DocumentExpiryDate, Guid FileRevisionId, DateOnly DueDate, string? Instruction, Guid IssuedByUserId, DateTime IssuedAtUtc, bool IsCancelled, string? CancellationReason, IReadOnlyList<DocumentDistributionRecipientDto> Recipients);
internal sealed record IssueDocumentDistributionRequest(DateOnly? DueDate, string? Instruction, IReadOnlyList<Guid>? RecipientUserIds);
internal sealed record CancelDocumentDistributionRequest(string? Reason);
internal sealed record DocumentDistributionRecipientPickerDto(Guid UserId, string Email, bool IsActive, bool CanReadDocuments);

internal sealed record GetDocumentRequirementsQuery(Guid? VesselId, bool? IsEnabled, int? PageNumber, int? PageSize);
internal sealed record DocumentRequirementRowDto(
    Guid Id,
    Guid VesselId,
    int DocumentTypeId,
    string DocumentTypeName,
    bool IsEnabled,
    int MinimumRemainingValidityDays,
    string? Instruction,
    int Revision,
    Guid UpdatedByUserId,
    DateTime UpdatedAtUtc,
    Guid Version);
internal sealed record DocumentRequirementsPageDto(
    IReadOnlyList<DocumentRequirementRowDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
internal sealed record DocumentRequirementHistoryItemDto(
    Guid Id,
    int Revision,
    string EventType,
    string? Reason,
    bool IsEnabled,
    int MinimumRemainingValidityDays,
    string? Instruction,
    Guid ActorUserId,
    string? ActorEmail,
    DateTime OccurredAtUtc);
internal sealed record DocumentRequirementDetailsDto(
    Guid Id,
    Guid VesselId,
    int DocumentTypeId,
    string DocumentTypeName,
    bool IsEnabled,
    int MinimumRemainingValidityDays,
    string? Instruction,
    int Revision,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid UpdatedByUserId,
    DateTime UpdatedAtUtc,
    Guid Version,
    IReadOnlyList<DocumentRequirementHistoryItemDto> History);
internal sealed record CreateDocumentRequirementRequest(Guid? VesselId, int? DocumentTypeId, int? MinimumRemainingValidityDays, string? Instruction, string? Reason);
internal sealed record UpdateDocumentRequirementRequest(bool? IsEnabled, int? MinimumRemainingValidityDays, string? Instruction, string? Reason, Guid? Version);
internal sealed record DisableDocumentRequirementRequest(string? Reason, Guid? Version);

internal sealed record GetDocumentReadinessQuery(Guid? VesselId, DateOnly? RequiredThrough, string? Status, int? PageNumber, int? PageSize);
internal sealed record DocumentReadinessSummaryDto(int ConfiguredCount, int SatisfiedCount, int UnsatisfiedCount);
internal sealed record DocumentReadinessCandidateDto(
    Guid DocumentId,
    string Number,
    string Status,
    string ReviewStatus,
    bool HasFile,
    string RetentionState,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    bool Qualifies,
    IReadOnlyList<string> Blockers);
internal sealed record DocumentReadinessRowDto(
    Guid RequirementId,
    Guid VesselId,
    int DocumentTypeId,
    string DocumentTypeName,
    int MinimumRemainingValidityDays,
    string? Instruction,
    DateOnly RequiredValidUntil,
    string Status,
    IReadOnlyList<string> Blockers,
    Guid? SelectedDocumentId,
    IReadOnlyList<DocumentReadinessCandidateDto> Candidates,
    DocumentReadinessDistributionDto? Distribution);
internal sealed record DocumentReadinessRecipientDto(
    Guid UserId,
    string? Email,
    string State,
    bool IsOverdue,
    DateTime? AcknowledgedAtUtc,
    DateTime? StateChangedAtUtc,
    string? StateReason);
internal sealed record DocumentReadinessDistributionDto(
    string Status,
    Guid? IssueId,
    DateTime? IssuedAtUtc,
    DateOnly? DueDate,
    int PendingCount,
    int AcknowledgedCount,
    int OverdueCount,
    IReadOnlyList<DocumentReadinessRecipientDto>? Recipients);
internal sealed record DocumentReadinessPageDto(
    Guid? VesselId,
    string? VesselStatus,
    DateOnly Today,
    DateOnly RequiredThrough,
    DocumentReadinessSummaryDto Summary,
    IReadOnlyList<DocumentReadinessRowDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
internal sealed record GetVesselReadinessOverviewQuery(DateOnly? RequiredThrough, int? PageNumber, int? PageSize);
internal sealed record VesselReadinessOverviewRowDto(
    Guid VesselId,
    string VesselName,
    string Status,
    DocumentReadinessSummaryDto Summary);
internal sealed record VesselReadinessOverviewPageDto(
    DateOnly Today,
    DateOnly RequiredThrough,
    IReadOnlyList<VesselReadinessOverviewRowDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record DocumentsApiResult<T>(T? Value, DocumentsApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record DocumentsApiFailure(
    int StatusCode,
    string Title,
    string Message,
    IReadOnlyDictionary<string, string[]> Errors)
{
    internal bool IsValidation => StatusCode == StatusCodes.Status400BadRequest;

    internal bool IsForbidden => StatusCode == StatusCodes.Status403Forbidden;

    internal bool IsConflict => StatusCode == StatusCodes.Status409Conflict;

    internal bool IsNotFound => StatusCode == StatusCodes.Status404NotFound;

    internal bool IsPayloadTooLarge => StatusCode == StatusCodes.Status413PayloadTooLarge;
}
