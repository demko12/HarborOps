using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class CancelCrewAvailabilityRequestService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CrewAvailabilityDecisionResponse> CancelAsync(
        Guid requestId,
        Guid actorUserId,
        CancelCrewAvailabilityRequestRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CrewAvailabilityDecisionValidator.ValidateReason(request.Reason);
        if (errors.Count > 0)
        {
            throw new CancelCrewAvailabilityRequestValidationException(errors);
        }

        var availabilityRequest = await dbContext.Set<CrewAvailabilityRequest>()
            .SingleOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken)
            ?? throw new CrewAvailabilityRequestNotFoundException(requestId);

        var reason = request.Reason!.Trim();
        var previousStatus = availabilityRequest.Status;
        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(occurredAtUtc);

        availabilityRequest.Cancel(actorUserId, reason, occurredAtUtc, today);

        var transition = CrewAvailabilityTransition.RecordCancelled(
            requestId, actorUserId, reason, occurredAtUtc, previousStatus);
        await dbContext.Set<CrewAvailabilityTransition>().AddAsync(transition, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CrewAvailabilityDecisionResponse(
            requestId,
            availabilityRequest.CrewMemberId,
            previousStatus,
            availabilityRequest.Status,
            actorUserId,
            reason,
            occurredAtUtc);
    }
}
