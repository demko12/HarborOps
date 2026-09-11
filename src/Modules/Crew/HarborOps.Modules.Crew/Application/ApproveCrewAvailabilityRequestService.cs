using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class ApproveCrewAvailabilityRequestService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CrewAvailabilityDecisionResponse> ApproveAsync(
        Guid requestId,
        Guid reviewerUserId,
        CancellationToken cancellationToken)
    {
        var availabilityRequest = await dbContext.Set<CrewAvailabilityRequest>()
            .SingleOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken)
            ?? throw new CrewAvailabilityRequestNotFoundException(requestId);

        if (availabilityRequest.Status != CrewAvailabilityRequest.RequestedStatus)
        {
            throw new InvalidCrewAvailabilityTransitionException(
                requestId, availabilityRequest.Status, CrewAvailabilityRequest.ApproveAction);
        }

        if (reviewerUserId == availabilityRequest.RequestedByUserId)
        {
            throw new CrewAvailabilitySelfApprovalException(requestId);
        }

        var previousStatus = availabilityRequest.Status;
        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (dbContext.Database.IsRelational())
        {
            await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await ApplyAsync(availabilityRequest, reviewerUserId, occurredAtUtc, cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
        }
        else
        {
            await ApplyAsync(availabilityRequest, reviewerUserId, occurredAtUtc, cancellationToken);
        }

        return new CrewAvailabilityDecisionResponse(
            requestId,
            availabilityRequest.CrewMemberId,
            previousStatus,
            availabilityRequest.Status,
            reviewerUserId,
            Reason: null,
            occurredAtUtc);
    }

    private async Task ApplyAsync(
        CrewAvailabilityRequest availabilityRequest,
        Guid reviewerUserId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var crewMemberId = availabilityRequest.CrewMemberId;
        var startDate = availabilityRequest.StartDate;
        var endDate = availabilityRequest.EndDate;

        // Re-run every conflict check inside the transaction so a decision that raced ahead of us
        // (another approval, a fresh assignment) is caught before we commit — and so a rejected
        // approval leaves no transition row behind.
        await CrewAvailabilityOverlap.EnsureNoOverlappingRequestAsync(
            dbContext, crewMemberId, startDate, endDate, availabilityRequest.Id, cancellationToken);

        var assignmentConflict = await dbContext.Set<CrewAssignment>()
            .AsNoTracking()
            .Where(assignment => assignment.CrewMemberId == crewMemberId
                && assignment.ActualSignOffDate == null
                && (assignment.Status == CrewAssignment.PlannedStatus || assignment.Status == CrewAssignment.ActiveStatus)
                && assignment.SignOnDate <= endDate
                && startDate <= assignment.PlannedSignOffDate)
            .OrderBy(assignment => assignment.SignOnDate)
            .ThenBy(assignment => assignment.Id)
            .Select(assignment => new { assignment.SignOnDate, assignment.PlannedSignOffDate, assignment.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (assignmentConflict is not null)
        {
            var recordType = assignmentConflict.Status == CrewAssignment.ActiveStatus
                ? "ActiveAssignment"
                : "PlannedAssignment";
            throw new CrewAvailabilityApprovalConflictException(
                crewMemberId, assignmentConflict.SignOnDate, assignmentConflict.PlannedSignOffDate, recordType);
        }

        var reliefConflict = await dbContext.Set<CrewRotation>()
            .AsNoTracking()
            .Where(rotation => rotation.RelievingCrewMemberId == crewMemberId
                && rotation.Status == CrewRotation.PlannedStatus
                && rotation.PlannedReliefDate >= startDate
                && rotation.PlannedReliefDate <= endDate)
            .OrderBy(rotation => rotation.PlannedReliefDate)
            .ThenBy(rotation => rotation.Id)
            .Select(rotation => new { rotation.PlannedReliefDate })
            .FirstOrDefaultAsync(cancellationToken);

        if (reliefConflict is not null)
        {
            throw new CrewAvailabilityApprovalConflictException(
                crewMemberId, reliefConflict.PlannedReliefDate, reliefConflict.PlannedReliefDate, "PlannedReliefRotation");
        }

        availabilityRequest.Approve(reviewerUserId, occurredAtUtc);

        var transition = CrewAvailabilityTransition.RecordApproved(
            availabilityRequest.Id, reviewerUserId, occurredAtUtc);
        await dbContext.Set<CrewAvailabilityTransition>().AddAsync(transition, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
