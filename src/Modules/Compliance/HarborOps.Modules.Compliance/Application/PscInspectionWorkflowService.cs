using System.Data;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HarborOps.Modules.Compliance.Application;

public sealed class PscInspectionWorkflowService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<RegisterPscDeficiencyResponse> RegisterDeficiencyAsync(
        int pscInspectionId,
        RegisterPscDeficiencyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = ValidateDeficiency(request);
        if (errors.Count > 0) throw new PscInspectionWorkflowValidationException(errors);

        var inspectionExists = await dbContext.Set<PscInspection>()
            .AsNoTracking()
            .AnyAsync(inspection => inspection.Id == pscInspectionId, cancellationToken);
        if (!inspectionExists) throw new PscInspectionNotFoundException(pscInspectionId);

        var nonConformity = NonConformity.Register(
            request.EvidenceDocumentId!.Value,
            request.Severity!.Trim(),
            request.CorrectiveActionDeadline!.Value,
            pscId: pscInspectionId,
            pscDeficiencyCode: request.DeficiencyCode!.Trim(),
            pscDeficiencyDescription: request.Description!.Trim());

        await dbContext.Set<NonConformity>().AddAsync(nonConformity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new RegisterPscDeficiencyResponse(nonConformity.Id);
    }

    public async Task DetainAsync(
        int pscInspectionId,
        DetainPscInspectionRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = ValidateDetain(request);
        if (errors.Count > 0) throw new PscInspectionWorkflowValidationException(errors);

        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
        var inspection = await dbContext.Set<PscInspection>()
            .SingleOrDefaultAsync(candidate => candidate.Id == pscInspectionId, cancellationToken)
            ?? throw new PscInspectionNotFoundException(pscInspectionId);

        var latestDecision = await dbContext.Set<PscDetentionDecision>()
            .Where(decision => decision.PscInspectionId == pscInspectionId)
            .OrderByDescending(decision => decision.OccurredAtUtc)
            .ThenByDescending(decision => decision.Id)
            .Select(decision => decision.Decision)
            .FirstOrDefaultAsync(cancellationToken);
        var state = PscDetentionState.FromLatestDecision(latestDecision, inspection.VesselDetained);
        if (state != PscDetentionState.NotDetained)
            throw new PscInspectionWorkflowConflictException("The PSC inspection is already detained or released.");

        var hasOpenCriticalDeficiency = await dbContext.Set<NonConformity>()
            .AnyAsync(nonConformity => nonConformity.PscId == pscInspectionId
                && nonConformity.Severity == NonConformity.SeverityCritical
                && nonConformity.ClosureStatus != NonConformity.ClosureStatusClosed, cancellationToken);
        if (!hasOpenCriticalDeficiency)
            throw new PscInspectionWorkflowConflictException("Detention requires an open Critical PSC deficiency.");

        await dbContext.Set<PscDetentionDecision>().AddAsync(
            PscDetentionDecision.RecordDetained(
                pscInspectionId,
                request.AuthorityReference!.Trim(),
                request.Reason!.Trim(),
                actorUserId,
                timeProvider.GetUtcNow()),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReleaseAsync(
        int pscInspectionId,
        ReleasePscInspectionRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = ValidateRelease(request);
        if (errors.Count > 0) throw new PscInspectionWorkflowValidationException(errors);

        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
        var inspection = await dbContext.Set<PscInspection>()
            .SingleOrDefaultAsync(candidate => candidate.Id == pscInspectionId, cancellationToken)
            ?? throw new PscInspectionNotFoundException(pscInspectionId);

        var latestDecision = await dbContext.Set<PscDetentionDecision>()
            .Where(decision => decision.PscInspectionId == pscInspectionId)
            .OrderByDescending(decision => decision.OccurredAtUtc)
            .ThenByDescending(decision => decision.Id)
            .Select(decision => decision.Decision)
            .FirstOrDefaultAsync(cancellationToken);
        var state = PscDetentionState.FromLatestDecision(latestDecision, inspection.VesselDetained);
        if (state != PscDetentionState.Detained)
            throw new PscInspectionWorkflowConflictException("Only a detained PSC inspection can be released.");

        var deficiencyCounts = await dbContext.Set<NonConformity>()
            .Where(nonConformity => nonConformity.PscId == pscInspectionId)
            .GroupBy(_ => 1)
            .Select(group => new { Registered = group.Count(), Open = group.Count(nonConformity => nonConformity.ClosureStatus != NonConformity.ClosureStatusClosed) })
            .SingleOrDefaultAsync(cancellationToken);
        var registeredCount = deficiencyCounts?.Registered ?? 0;
        var openCount = deficiencyCounts?.Open ?? 0;
        if (registeredCount != inspection.DeficiencyCount)
            throw new PscInspectionWorkflowConflictException("All reported PSC deficiencies must be registered before release.");
        if (openCount > 0)
            throw new PscInspectionWorkflowConflictException("All PSC deficiencies must be closed before release.");

        await dbContext.Set<PscDetentionDecision>().AddAsync(
            PscDetentionDecision.RecordReleased(
                pscInspectionId,
                request.AuthorityReference!.Trim(),
                request.ReleaseDate!.Value,
                request.DocumentId,
                actorUserId,
                timeProvider.GetUtcNow()),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private IReadOnlyDictionary<string, string[]> ValidateDeficiency(RegisterPscDeficiencyRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ValidateRequiredText(request.DeficiencyCode, nameof(request.DeficiencyCode), NonConformity.PscDeficiencyCodeMaxLength, errors);
        ValidateRequiredText(request.Description, nameof(request.Description), NonConformity.PscDeficiencyDescriptionMaxLength, errors);
        if (request.EvidenceDocumentId is null || request.EvidenceDocumentId == Guid.Empty) errors[nameof(request.EvidenceDocumentId)] = ["Evidence document ID is required."];
        if (request.Severity?.Trim() is not (NonConformity.SeverityMinor or NonConformity.SeverityMajor or NonConformity.SeverityCritical)) errors[nameof(request.Severity)] = ["Severity must be Minor, Major, or Critical."];
        if (request.CorrectiveActionDeadline is null) errors[nameof(request.CorrectiveActionDeadline)] = ["Corrective action deadline is required."];
        else if (request.CorrectiveActionDeadline <= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)) errors[nameof(request.CorrectiveActionDeadline)] = ["Corrective action deadline must be a future date."];
        return errors;
    }

    private static IReadOnlyDictionary<string, string[]> ValidateDetain(DetainPscInspectionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ValidateRequiredText(request.AuthorityReference, nameof(request.AuthorityReference), PscDetentionDecision.AuthorityReferenceMaxLength, errors);
        ValidateRequiredText(request.Reason, nameof(request.Reason), PscDetentionDecision.ReasonMaxLength, errors);
        return errors;
    }

    private static IReadOnlyDictionary<string, string[]> ValidateRelease(ReleasePscInspectionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ValidateRequiredText(request.AuthorityReference, nameof(request.AuthorityReference), PscDetentionDecision.AuthorityReferenceMaxLength, errors);
        if (request.ReleaseDate is null) errors[nameof(request.ReleaseDate)] = ["Release date is required."];
        return errors;
    }

    private static void ValidateRequiredText(string? value, string field, int maximumLength, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors[field] = [$"{field} is required."];
        else if (value.Trim().Length > maximumLength) errors[field] = [$"{field} must not exceed {maximumLength} characters."];
    }
}
