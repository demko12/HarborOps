using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class RejectCrewAvailabilityRequestService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CrewAvailabilityDecisionResponse> RejectAsync(
        Guid requestId,
        Guid reviewerUserId,
        RejectCrewAvailabilityRequestRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CrewAvailabilityDecisionValidator.ValidateReason(request.Reason);
        if (errors.Count > 0)
        {
            throw new RejectCrewAvailabilityRequestValidationException(errors);
        }

        var availabilityRequest = await dbContext.Set<CrewAvailabilityRequest>()
            .SingleOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken)
            ?? throw new CrewAvailabilityRequestNotFoundException(requestId);

        var reason = request.Reason!.Trim();
        var previousStatus = availabilityRequest.Status;
        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        availabilityRequest.Reject(reviewerUserId, reason, occurredAtUtc);

        var transition = CrewAvailabilityTransition.RecordRejected(requestId, reviewerUserId, reason, occurredAtUtc);
        await dbContext.Set<CrewAvailabilityTransition>().AddAsync(transition, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CrewAvailabilityDecisionResponse(
            requestId,
            availabilityRequest.CrewMemberId,
            previousStatus,
            availabilityRequest.Status,
            reviewerUserId,
            reason,
            occurredAtUtc);
    }
}
