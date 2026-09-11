using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal static class CrewRotationEligibility
{
    internal static bool IsAssignmentEligibleForRotation(CrewAssignment assignment) =>
        assignment.ActualSignOffDate is null
        && (assignment.Status == CrewAssignment.PlannedStatus || assignment.Status == CrewAssignment.ActiveStatus);

    internal static async Task EnsureRelievingCrewMemberAvailableAsync(
        DbContext dbContext,
        Guid assignedCrewMemberId,
        Guid relievingCrewMemberId,
        DateOnly reliefStartDate,
        DateOnly reliefEndDate,
        CancellationToken cancellationToken)
    {
        if (relievingCrewMemberId == assignedCrewMemberId)
        {
            throw new SelfRelievingCrewMemberException(relievingCrewMemberId);
        }

        var relievingMemberExists = await dbContext.Set<CrewMember>()
            .AsNoTracking()
            .AnyAsync(member => member.Id == relievingCrewMemberId, cancellationToken);

        if (!relievingMemberExists)
        {
            throw new CrewMemberNotFoundException(relievingCrewMemberId);
        }

        var relievingMemberHasOpenAssignment = await dbContext.Set<CrewAssignment>()
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.CrewMemberId == relievingCrewMemberId
                    && candidate.ActualSignOffDate == null
                    && (candidate.Status == CrewAssignment.PlannedStatus
                        || candidate.Status == CrewAssignment.ActiveStatus),
                cancellationToken);

        if (relievingMemberHasOpenAssignment)
        {
            throw new RelievingCrewMemberUnavailableException(relievingCrewMemberId);
        }

        var unavailability = await CrewAvailabilityOverlap.FindApprovedUnavailabilityAsync(
            dbContext, relievingCrewMemberId, reliefStartDate, reliefEndDate, cancellationToken);

        if (unavailability is { } hit)
        {
            throw new CrewMemberUnavailableException(
                relievingCrewMemberId, hit.StartDate, hit.EndDate, "ApprovedUnavailability");
        }
    }
}
