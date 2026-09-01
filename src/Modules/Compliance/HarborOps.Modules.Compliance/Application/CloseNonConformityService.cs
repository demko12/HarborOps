using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

public sealed class CloseNonConformityService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CloseNonConformityResponse> CloseAsync(
        int nonConformityId,
        Guid closedByUserId,
        CloseNonConformityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CloseNonConformityValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new CloseNonConformityValidationException(errors);
        }

        var nonConformity = await dbContext.Set<NonConformity>()
            .FirstOrDefaultAsync(nonConformity => nonConformity.Id == nonConformityId, cancellationToken)
            ?? throw new NonConformityNotFoundException(nonConformityId);

        nonConformity.Close(
            request.ClosureNotes!.Trim(),
            closedByUserId,
            timeProvider.GetUtcNow(),
            request.FinalEvidenceDocumentId);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CloseNonConformityResponse(
            nonConformity.Id,
            nonConformity.ClosureStatus,
            nonConformity.ClosedAtUtc!.Value);
    }
}
