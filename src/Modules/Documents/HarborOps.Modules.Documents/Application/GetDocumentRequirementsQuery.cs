using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetDocumentRequirementsQuery(
    DbContext dbContext,
    IDocumentDistributionRecipientLookup actorLookup)
    : IGetDocumentRequirementsQuery
{
    internal const string VesselIdField = "vesselId";
    internal const string DocumentTypeIdField = "documentTypeId";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    public async Task<GetDocumentRequirementsResponse> GetAsync(
        GetDocumentRequirementsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            throw new GetDocumentRequirementsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? DefaultPageNumber;
        var pageSize = request.PageSize ?? DefaultPageSize;

        var requirements = dbContext.Set<VesselDocumentRequirement>().AsNoTracking();

        if (request.VesselId is { } vesselId)
        {
            requirements = requirements.Where(requirement => requirement.VesselId == vesselId);
        }

        if (request.DocumentTypeId is { } documentTypeId)
        {
            requirements = requirements.Where(requirement => requirement.DocumentTypeId == documentTypeId);
        }

        if (request.IsEnabled is { } isEnabled)
        {
            requirements = requirements.Where(requirement => requirement.IsEnabled == isEnabled);
        }

        var rows =
            from requirement in requirements
            join type in dbContext.Set<DocumentType>().AsNoTracking()
                on requirement.DocumentTypeId equals type.Id
            select new { Requirement = requirement, TypeName = type.Name };

        var totalCount = await rows.CountAsync(cancellationToken);

        var items = await rows
            .OrderBy(row => row.Requirement.VesselId)
            .ThenBy(row => row.TypeName)
            .ThenBy(row => row.Requirement.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new DocumentRequirementListItem(
                row.Requirement.Id,
                row.Requirement.VesselId,
                row.Requirement.DocumentTypeId,
                row.TypeName,
                row.Requirement.IsEnabled,
                row.Requirement.MinimumRemainingValidityDays,
                row.Requirement.Instruction,
                row.Requirement.Revision,
                row.Requirement.UpdatedByUserId,
                row.Requirement.UpdatedAtUtc,
                row.Requirement.ConcurrencyToken))
            .ToListAsync(cancellationToken);

        return new GetDocumentRequirementsResponse(items, pageNumber, pageSize, totalCount);
    }

    public async Task<DocumentRequirementDetailsResponse> GetByIdAsync(
        Guid requirementId,
        CancellationToken cancellationToken)
    {
        var row = await (
            from requirement in dbContext.Set<VesselDocumentRequirement>().AsNoTracking()
            join type in dbContext.Set<DocumentType>().AsNoTracking()
                on requirement.DocumentTypeId equals type.Id
            where requirement.Id == requirementId
            select new { Requirement = requirement, TypeName = type.Name })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DocumentRequirementNotFoundException(requirementId);

        var entries = await dbContext.Set<VesselDocumentRequirementHistory>()
            .AsNoTracking()
            .Where(entry => entry.RequirementId == requirementId)
            .OrderByDescending(entry => entry.Revision)
            .ToListAsync(cancellationToken);

        var actorEmails = entries.Count == 0
            ? new Dictionary<Guid, string>()
            : (await actorLookup.LookupAsync(
                entries.Select(entry => entry.ActorUserId).Distinct().ToList(),
                cancellationToken))
                .ToDictionary(identity => identity.UserId, identity => identity.Email);

        var history = entries
            .Select(entry => new DocumentRequirementHistoryItem(
                entry.Id,
                entry.Revision,
                entry.EventType,
                entry.Reason,
                entry.IsEnabled,
                entry.MinimumRemainingValidityDays,
                entry.Instruction,
                entry.ActorUserId,
                actorEmails.GetValueOrDefault(entry.ActorUserId),
                entry.OccurredAtUtc))
            .ToList();

        var current = row.Requirement;

        return new DocumentRequirementDetailsResponse(
            current.Id,
            current.VesselId,
            current.DocumentTypeId,
            row.TypeName,
            current.IsEnabled,
            current.MinimumRemainingValidityDays,
            current.Instruction,
            current.Revision,
            current.CreatedByUserId,
            current.CreatedAtUtc,
            current.UpdatedByUserId,
            current.UpdatedAtUtc,
            current.ConcurrencyToken,
            history);
    }

    internal static IReadOnlyDictionary<string, string[]> Validate(GetDocumentRequirementsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.VesselId == Guid.Empty)
        {
            errors[VesselIdField] = ["Vessel is required when supplied."];
        }

        if (request.DocumentTypeId is < 1)
        {
            errors[DocumentTypeIdField] = ["Document type must be a positive identifier when supplied."];
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
}
