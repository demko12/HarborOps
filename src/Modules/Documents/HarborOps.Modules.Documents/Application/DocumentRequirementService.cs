using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

public sealed class DocumentRequirementService(
    DbContext dbContext,
    IVesselExistenceLookup vesselExistenceLookup,
    TimeProvider timeProvider)
{
    public async Task<DocumentRequirementListItem> CreateAsync(
        Guid actorUserId,
        CreateDocumentRequirementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = DocumentRequirementValidator.ValidateCreate(request);
        if (errors.Count > 0)
        {
            throw new DocumentRequirementValidationException(errors);
        }

        var vesselId = request.VesselId!.Value;
        var documentTypeId = request.DocumentTypeId!.Value;

        var vessel = await vesselExistenceLookup.GetAsync(vesselId, cancellationToken);
        if (vessel is null)
        {
            throw new DocumentRequirementVesselNotFoundException(vesselId);
        }

        var documentTypeName = await dbContext.Set<DocumentType>()
            .AsNoTracking()
            .Where(type => type.Id == documentTypeId)
            .Select(type => type.Name)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DocumentTypeNotFoundException(documentTypeId);

        var alreadyConfigured = await dbContext.Set<VesselDocumentRequirement>()
            .AsNoTracking()
            .AnyAsync(
                requirement => requirement.VesselId == vesselId && requirement.DocumentTypeId == documentTypeId,
                cancellationToken);

        if (alreadyConfigured)
        {
            throw new DuplicateDocumentRequirementException(vesselId, documentTypeId);
        }

        var requirement = VesselDocumentRequirement.Create(
            vesselId,
            documentTypeId,
            request.MinimumRemainingValidityDays!.Value,
            DocumentRequirementValidator.NormalizeInstruction(request.Instruction),
            actorUserId,
            timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.Set<VesselDocumentRequirement>().AddAsync(requirement, cancellationToken);
        await dbContext.Set<VesselDocumentRequirementHistory>()
            .AddAsync(VesselDocumentRequirementHistory.RecordCreated(requirement, request.Reason!.Trim()), cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new DuplicateDocumentRequirementException(vesselId, documentTypeId, exception);
        }

        return ToListItem(requirement, documentTypeName);
    }

    public async Task<DocumentRequirementListItem> UpdateAsync(
        Guid requirementId,
        Guid actorUserId,
        UpdateDocumentRequirementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = DocumentRequirementValidator.ValidateUpdate(request);
        if (errors.Count > 0)
        {
            throw new DocumentRequirementValidationException(errors);
        }

        var requirement = await LoadForChangeAsync(requirementId, request.Version!.Value, cancellationToken);

        requirement.Update(
            request.IsEnabled!.Value,
            request.MinimumRemainingValidityDays!.Value,
            DocumentRequirementValidator.NormalizeInstruction(request.Instruction),
            actorUserId,
            timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.Set<VesselDocumentRequirementHistory>().AddAsync(
            VesselDocumentRequirementHistory.RecordUpdated(requirement, request.Reason!.Trim()),
            cancellationToken);

        await SaveChangeAsync(requirementId, cancellationToken);

        return ToListItem(requirement, await GetDocumentTypeNameAsync(requirement.DocumentTypeId, cancellationToken));
    }

    public async Task<DocumentRequirementListItem> DisableAsync(
        Guid requirementId,
        Guid actorUserId,
        DisableDocumentRequirementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = DocumentRequirementValidator.ValidateDisable(request);
        if (errors.Count > 0)
        {
            throw new DocumentRequirementValidationException(errors);
        }

        var requirement = await LoadForChangeAsync(requirementId, request.Version!.Value, cancellationToken);

        requirement.Disable(actorUserId, timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.Set<VesselDocumentRequirementHistory>().AddAsync(
            VesselDocumentRequirementHistory.RecordDisabled(requirement, request.Reason!.Trim()),
            cancellationToken);

        await SaveChangeAsync(requirementId, cancellationToken);

        return ToListItem(requirement, await GetDocumentTypeNameAsync(requirement.DocumentTypeId, cancellationToken));
    }

    private async Task<VesselDocumentRequirement> LoadForChangeAsync(
        Guid requirementId,
        Guid expectedVersion,
        CancellationToken cancellationToken)
    {
        var requirement = await dbContext.Set<VesselDocumentRequirement>()
            .SingleOrDefaultAsync(candidate => candidate.Id == requirementId, cancellationToken)
            ?? throw new DocumentRequirementNotFoundException(requirementId);

        if (requirement.ConcurrencyToken != expectedVersion)
        {
            throw new DocumentRequirementConflictException(requirementId);
        }

        return requirement;
    }

    private async Task SaveChangeAsync(Guid requirementId, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new DocumentRequirementConflictException(requirementId, exception);
        }
    }

    private Task<string> GetDocumentTypeNameAsync(int documentTypeId, CancellationToken cancellationToken) =>
        dbContext.Set<DocumentType>()
            .AsNoTracking()
            .Where(type => type.Id == documentTypeId)
            .Select(type => type.Name)
            .SingleAsync(cancellationToken);

    private static DocumentRequirementListItem ToListItem(VesselDocumentRequirement requirement, string documentTypeName) =>
        new(
            requirement.Id,
            requirement.VesselId,
            requirement.DocumentTypeId,
            documentTypeName,
            requirement.IsEnabled,
            requirement.MinimumRemainingValidityDays,
            requirement.Instruction,
            requirement.Revision,
            requirement.UpdatedByUserId,
            requirement.UpdatedAtUtc,
            requirement.ConcurrencyToken);
}
