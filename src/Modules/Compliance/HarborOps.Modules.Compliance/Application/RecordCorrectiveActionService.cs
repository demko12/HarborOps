using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

public sealed class RecordCorrectiveActionService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<RecordCorrectiveActionResponse> RecordAsync(
        int nonConformityId,
        Guid actorUserId,
        RecordCorrectiveActionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RecordCorrectiveActionValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RecordCorrectiveActionValidationException(errors);
        }

        var nonConformity = await dbContext.Set<NonConformity>()
            .Include(nonConformity => nonConformity.CorrectiveActions)
            .FirstOrDefaultAsync(nonConformity => nonConformity.Id == nonConformityId, cancellationToken)
            ?? throw new NonConformityNotFoundException(nonConformityId);

        var correctiveAction = nonConformity.AddCorrectiveAction(
            request.ActionText!.Trim(),
            actorUserId,
            timeProvider.GetUtcNow(),
            request.EvidenceDocumentId);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordCorrectiveActionResponse(correctiveAction.Id, nonConformity.ClosureStatus);
    }
}
