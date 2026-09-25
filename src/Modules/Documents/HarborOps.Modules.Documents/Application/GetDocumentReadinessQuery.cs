using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetDocumentReadinessQuery(
    DbContext dbContext,
    IVesselExistenceLookup vesselExistenceLookup,
    IDocumentDistributionRecipientLookup recipientLookup,
    TimeProvider timeProvider)
    : IGetDocumentReadinessQuery
{
    internal const string VesselIdField = "vesselId";
    internal const string RequiredThroughField = "requiredThrough";
    internal const string StatusField = "status";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    private static readonly string[] ReadOnlyVisibleStatuses = [Document.ActiveStatus, Document.SupersededStatus];

    public async Task<GetDocumentReadinessResponse> GetAsync(
        GetDocumentReadinessRequest request,
        bool restrictToReadOnlyVisible,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var errors = Validate(request, today);
        if (errors.Count > 0)
        {
            throw new GetDocumentReadinessValidationException(errors);
        }

        var requiredThrough = request.RequiredThrough ?? today;
        var pageNumber = request.PageNumber ?? DefaultPageNumber;
        var pageSize = request.PageSize ?? DefaultPageSize;

        if (request.VesselId is { } requestedVesselId
            && await vesselExistenceLookup.GetAsync(requestedVesselId, cancellationToken) is null)
        {
            throw new DocumentRequirementVesselNotFoundException(requestedVesselId);
        }

        var requirements = dbContext.Set<VesselDocumentRequirement>()
            .AsNoTracking()
            .Where(requirement => requirement.IsEnabled);

        if (request.VesselId is { } vesselId)
        {
            requirements = requirements.Where(requirement => requirement.VesselId == vesselId);
        }

        var documents = dbContext.Set<Document>().AsNoTracking();
        var retentionStates = dbContext.Set<DocumentRetentionState>().AsNoTracking();

        var rows = RequirementRows(dbContext, requirements, today, requiredThrough);

        var configuredCount = await rows.CountAsync(cancellationToken);
        var satisfiedCount = await rows.CountAsync(row => row.Satisfied, cancellationToken);

        if (string.Equals(request.Status, DocumentReadinessRules.SatisfiedStatus, StringComparison.Ordinal))
        {
            rows = rows.Where(row => row.Satisfied);
        }
        else if (string.Equals(request.Status, DocumentReadinessRules.UnsatisfiedStatus, StringComparison.Ordinal))
        {
            rows = rows.Where(row => !row.Satisfied);
        }

        var totalCount = await rows.CountAsync(cancellationToken);

        var page = await rows
            .OrderBy(row => row.VesselId)
            .ThenBy(row => row.TypeName)
            .ThenBy(row => row.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var vesselIds = page.Select(row => row.VesselId).Distinct().ToList();
        var documentTypeIds = page.Select(row => row.DocumentTypeId).Distinct().ToList();

        var pageDocuments = vesselIds.Count == 0
            ? []
            : await (
                from document in documents
                where vesselIds.Contains(document.VesselId) && documentTypeIds.Contains(document.DocumentTypeId)
                join state in retentionStates on document.Id equals state.DocumentId into states
                from state in states.DefaultIfEmpty()
                select new
                {
                    document.VesselId,
                    document.DocumentTypeId,
                    Candidate = new DocumentReadinessCandidate(
                        document.Id,
                        document.Number,
                        document.Status,
                        document.ReviewStatus,
                        document.StorageKey != null,
                        state == null ? DocumentRetentionState.ActiveState : state.State,
                        document.IssueDate,
                        document.ExpiryDate,
                        document.FileRevisionId,
                        document.ConcurrencyToken),
                })
                .ToListAsync(cancellationToken);

        var items = page
            .Select(row =>
            {
                var documentsOfType = pageDocuments
                    .Where(entry => entry.VesselId == row.VesselId && entry.DocumentTypeId == row.DocumentTypeId)
                    .Select(entry => entry.Candidate)
                    .ToList();

                return Evaluate(
                    row.Id,
                    row.VesselId,
                    row.DocumentTypeId,
                    row.TypeName,
                    row.MinimumRemainingValidityDays,
                    row.Instruction,
                    documentsOfType,
                    today,
                    requiredThrough,
                    restrictToReadOnlyVisible);
            })
            .ToList();

        var selectedDocuments = pageDocuments
            .Select(entry => entry.Candidate)
            .Where(candidate => items.Any(item => item.SelectedDocumentId == candidate.DocumentId))
            .ToList();

        var distributions = await GetDistributionsAsync(
            selectedDocuments,
            today,
            includeRecipients: !restrictToReadOnlyVisible,
            cancellationToken);

        items = items
            .Select(item => item.SelectedDocumentId is { } selectedId && distributions.TryGetValue(selectedId, out var distribution)
                ? item with { Distribution = distribution }
                : item)
            .ToList();

        var vesselStatus = request.VesselId is null
            ? null
            : DocumentReadinessRules.VesselStatus(configuredCount, satisfiedCount);

        return new GetDocumentReadinessResponse(
            request.VesselId,
            vesselStatus,
            today,
            requiredThrough,
            new DocumentReadinessSummary(configuredCount, satisfiedCount, configuredCount - satisfiedCount),
            items,
            pageNumber,
            pageSize,
            totalCount);
    }

    internal static IQueryable<ReadinessRequirementRow> RequirementRows(
        DbContext dbContext,
        IQueryable<VesselDocumentRequirement> requirements,
        DateOnly today,
        DateOnly requiredThrough)
    {
        var documents = dbContext.Set<Document>().AsNoTracking();
        var retentionStates = dbContext.Set<DocumentRetentionState>().AsNoTracking();

        return
            from requirement in requirements
            join type in dbContext.Set<DocumentType>().AsNoTracking()
                on requirement.DocumentTypeId equals type.Id
            select new ReadinessRequirementRow
            {
                Id = requirement.Id,
                VesselId = requirement.VesselId,
                DocumentTypeId = requirement.DocumentTypeId,
                TypeName = type.Name,
                MinimumRemainingValidityDays = requirement.MinimumRemainingValidityDays,
                Instruction = requirement.Instruction,
                Satisfied = documents.Any(document =>
                    document.VesselId == requirement.VesselId
                    && document.DocumentTypeId == requirement.DocumentTypeId
                    && document.Status == Document.ActiveStatus
                    && document.ReviewStatus == Document.ApprovedReviewStatus
                    && document.StorageKey != null
                    && document.IssueDate <= today
                    && document.ExpiryDate >= requiredThrough.AddDays(requirement.MinimumRemainingValidityDays)
                    && !retentionStates.Any(state =>
                        state.DocumentId == document.Id && state.State != DocumentRetentionState.ActiveState)),
            };
    }

    internal static DocumentReadinessItem Evaluate(
        Guid requirementId,
        Guid vesselId,
        int documentTypeId,
        string documentTypeName,
        int minimumRemainingValidityDays,
        string? instruction,
        IReadOnlyList<DocumentReadinessCandidate> documentsOfType,
        DateOnly today,
        DateOnly requiredThrough,
        bool restrictToReadOnlyVisible)
    {
        var threshold = DocumentReadinessRules.ValidityThreshold(requiredThrough, minimumRemainingValidityDays);

        if (documentsOfType.Count == 0)
        {
            return new DocumentReadinessItem(
                requirementId,
                vesselId,
                documentTypeId,
                documentTypeName,
                minimumRemainingValidityDays,
                instruction,
                threshold,
                DocumentReadinessRules.UnsatisfiedStatus,
                [DocumentReadinessRules.Missing],
                SelectedDocumentId: null,
                Candidates: [],
                Distribution: null);
        }

        var evaluated = DocumentReadinessRules.EvaluatedCandidates(documentsOfType)
            .Select(candidate => new
            {
                Candidate = candidate,
                Blockers = DocumentReadinessRules.EvaluateDocument(candidate, today, threshold),
            })
            .ToList();

        var qualifying = evaluated.Where(entry => entry.Blockers.Count == 0).ToList();

        var selected = DocumentReadinessRules.OrderForDisplay(
                qualifying,
                entry => entry.Candidate.ExpiryDate,
                entry => entry.Candidate.IssueDate,
                entry => entry.Candidate.DocumentId)
            .Select(entry => (Guid?)entry.Candidate.DocumentId)
            .FirstOrDefault();

        var blockers = qualifying.Count > 0
            ? []
            : DocumentReadinessRules.CombineBlockers(evaluated.Select(entry => entry.Blockers));

        var candidates = evaluated
            .Where(entry => !restrictToReadOnlyVisible || IsVisibleToReadOnlyUsers(entry.Candidate))
            .OrderBy(entry => entry.Blockers.Count == 0 ? 0 : 1)
            .ThenByDescending(entry => entry.Candidate.ExpiryDate)
            .ThenByDescending(entry => entry.Candidate.IssueDate)
            .ThenBy(entry => entry.Candidate.DocumentId)
            .Select(entry => new DocumentReadinessCandidateItem(
                entry.Candidate.DocumentId,
                entry.Candidate.Number,
                entry.Candidate.Status,
                entry.Candidate.ReviewStatus,
                entry.Candidate.HasFile,
                entry.Candidate.RetentionState,
                entry.Candidate.IssueDate,
                entry.Candidate.ExpiryDate,
                entry.Blockers.Count == 0,
                entry.Blockers))
            .ToList();

        return new DocumentReadinessItem(
            requirementId,
            vesselId,
            documentTypeId,
            documentTypeName,
            minimumRemainingValidityDays,
            instruction,
            threshold,
            qualifying.Count > 0 ? DocumentReadinessRules.SatisfiedStatus : DocumentReadinessRules.UnsatisfiedStatus,
            blockers,
            selected,
            candidates,
            Distribution: null);
    }

    private async Task<IReadOnlyDictionary<Guid, DocumentReadinessDistribution>> GetDistributionsAsync(
        IReadOnlyList<DocumentReadinessCandidate> selectedDocuments,
        DateOnly today,
        bool includeRecipients,
        CancellationToken cancellationToken)
    {
        if (selectedDocuments.Count == 0)
        {
            return new Dictionary<Guid, DocumentReadinessDistribution>();
        }

        var documentIds = selectedDocuments.Select(document => document.DocumentId).ToList();

        var issues = await dbContext.Set<DocumentDistributionIssue>()
            .AsNoTracking()
            .Where(issue => documentIds.Contains(issue.DocumentId) && issue.CancelledAtUtc == null)
            .Select(issue => new
            {
                issue.Id,
                issue.DocumentId,
                issue.FileRevisionId,
                issue.DocumentConcurrencyToken,
                issue.DocumentNumberSnapshot,
                issue.DocumentExpirySnapshot,
                issue.IssuedAtUtc,
                issue.DueDate,
            })
            .ToListAsync(cancellationToken);

        var currentIssues = selectedDocuments
            .Select(document => new
            {
                document.DocumentId,
                Issue = issues
                    .Where(issue => issue.DocumentId == document.DocumentId
                        && issue.FileRevisionId == document.FileRevisionId
                        && issue.DocumentConcurrencyToken == document.ConcurrencyToken
                        && issue.DocumentNumberSnapshot == document.Number
                        && issue.DocumentExpirySnapshot == document.ExpiryDate)
                    .OrderByDescending(issue => issue.IssuedAtUtc)
                    .ThenByDescending(issue => issue.Id)
                    .FirstOrDefault(),
            })
            .ToList();

        var issueIds = currentIssues
            .Where(entry => entry.Issue is not null)
            .Select(entry => entry.Issue!.Id)
            .ToList();

        var recipients = issueIds.Count == 0
            ? []
            : await dbContext.Set<DocumentDistributionRecipient>()
                .AsNoTracking()
                .Where(recipient => issueIds.Contains(recipient.IssueId))
                .Select(recipient => new
                {
                    recipient.IssueId,
                    recipient.RecipientUserId,
                    recipient.State,
                    recipient.AcknowledgedAtUtc,
                    recipient.StateChangedAtUtc,
                    recipient.StateReason,
                })
                .ToListAsync(cancellationToken);

        var emails = includeRecipients && recipients.Count > 0
            ? (await recipientLookup.LookupAsync(
                recipients.Select(recipient => recipient.RecipientUserId).Distinct().ToList(),
                cancellationToken))
                .ToDictionary(identity => identity.UserId, identity => identity.Email)
            : new Dictionary<Guid, string>();

        var distributions = new Dictionary<Guid, DocumentReadinessDistribution>();

        foreach (var entry in currentIssues)
        {
            if (entry.Issue is not { } issue)
            {
                distributions[entry.DocumentId] = new DocumentReadinessDistribution(
                    DocumentReadinessRules.NotIssuedDistributionStatus,
                    IssueId: null,
                    IssuedAtUtc: null,
                    DueDate: null,
                    PendingCount: 0,
                    AcknowledgedCount: 0,
                    OverdueCount: 0,
                    Recipients: includeRecipients ? [] : null);

                continue;
            }

            var issueRecipients = recipients
                .Where(recipient => recipient.IssueId == issue.Id)
                .Select(recipient => new DocumentReadinessRecipient(
                    recipient.RecipientUserId,
                    emails.GetValueOrDefault(recipient.RecipientUserId),
                    recipient.State,
                    recipient.State == DocumentDistributionRecipient.PendingState && issue.DueDate < today,
                    recipient.AcknowledgedAtUtc,
                    recipient.StateChangedAtUtc,
                    recipient.StateReason))
                .ToList();

            distributions[entry.DocumentId] = new DocumentReadinessDistribution(
                DocumentReadinessRules.IssuedDistributionStatus,
                issue.Id,
                issue.IssuedAtUtc,
                issue.DueDate,
                issueRecipients.Count(recipient => recipient.State == DocumentDistributionRecipient.PendingState && !recipient.IsOverdue),
                issueRecipients.Count(recipient => recipient.State == DocumentDistributionRecipient.AcknowledgedState),
                issueRecipients.Count(recipient => recipient.IsOverdue),
                includeRecipients
                    ? issueRecipients
                        .OrderBy(recipient => recipient.Email ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(recipient => recipient.UserId)
                        .ToList()
                    : null);
        }

        return distributions;
    }

    internal static IReadOnlyDictionary<string, string[]> Validate(GetDocumentReadinessRequest request, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.VesselId == Guid.Empty)
        {
            errors[VesselIdField] = ["Vessel is required when supplied."];
        }

        if (request.RequiredThrough is { } requiredThrough
            && (requiredThrough < today || requiredThrough > today.AddDays(DocumentReadinessRules.RequiredThroughHorizonDays)))
        {
            errors[RequiredThroughField] =
            [
                $"Required-through date must be between today and {DocumentReadinessRules.RequiredThroughHorizonDays} days ahead.",
            ];
        }

        if (request.Status is { } status
            && status is not (DocumentReadinessRules.SatisfiedStatus or DocumentReadinessRules.UnsatisfiedStatus))
        {
            errors[StatusField] =
                [$"Status must be '{DocumentReadinessRules.SatisfiedStatus}' or '{DocumentReadinessRules.UnsatisfiedStatus}'."];
        }

        if (request.PageNumber is { } pageNumber && pageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize is { } pageSize)
        {
            if (pageSize < 1)
            {
                errors[PageSizeField] = ["Page size must be 1 or greater."];
            }
            else if (pageSize > MaxPageSize)
            {
                errors[PageSizeField] = [$"Page size must not exceed {MaxPageSize}."];
            }
        }

        return errors;
    }

    private static bool IsVisibleToReadOnlyUsers(DocumentReadinessCandidate candidate) =>
        string.Equals(candidate.ReviewStatus, Document.ApprovedReviewStatus, StringComparison.Ordinal)
        && ReadOnlyVisibleStatuses.Contains(candidate.Status, StringComparer.Ordinal);
}

internal sealed class ReadinessRequirementRow
{
    public Guid Id { get; init; }
    public Guid VesselId { get; init; }
    public int DocumentTypeId { get; init; }
    public string TypeName { get; init; } = null!;
    public int MinimumRemainingValidityDays { get; init; }
    public string? Instruction { get; init; }
    public bool Satisfied { get; init; }
}
