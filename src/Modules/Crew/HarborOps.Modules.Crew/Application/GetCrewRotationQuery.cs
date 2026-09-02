using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetCrewRotationQuery(DbContext dbContext, IDateTimeProvider dateTimeProvider)
    : IGetCrewRotationQuery
{
    public async Task<CrewRotationListItem> GetAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignmentExists = await dbContext.Set<CrewAssignment>()
            .AsNoTracking()
            .AnyAsync(assignment => assignment.Id == assignmentId, cancellationToken);

        if (!assignmentExists)
        {
            throw new CrewAssignmentNotFoundException(assignmentId);
        }

        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow);

        var row = await (
            from rotation in dbContext.Set<CrewRotation>().AsNoTracking()
            join assignment in dbContext.Set<CrewAssignment>().AsNoTracking()
                on rotation.AssignmentId equals assignment.Id
            join member in dbContext.Set<CrewMember>().AsNoTracking()
                on assignment.CrewMemberId equals member.Id
            join position in dbContext.Set<CrewPosition>().AsNoTracking()
                on assignment.RankId equals position.Id
            join relievingMember in dbContext.Set<CrewMember>().AsNoTracking()
                on rotation.RelievingCrewMemberId equals relievingMember.Id into relievingJoin
            from relievingMember in relievingJoin.DefaultIfEmpty()
            where rotation.AssignmentId == assignmentId && rotation.Status == CrewRotation.PlannedStatus
            select new
            {
                rotation.Id,
                rotation.AssignmentId,
                assignment.VesselId,
                assignment.CrewMemberId,
                MemberName = member.FirstName + " " + member.LastName,
                assignment.RankId,
                RankName = position.RankName,
                rotation.RelievingCrewMemberId,
                RelievingCrewMemberName = relievingMember != null ? relievingMember.FirstName + " " + relievingMember.LastName : null,
                rotation.RotationPattern,
                rotation.PlannedReliefDate,
                assignment.ActualSignOffDate,
                rotation.Status,
                rotation.ExecutedAtUtc,
                rotation.ExecutedByUserId,
                rotation.ReplacementAssignmentId,
                rotation.CancelledAtUtc,
                rotation.CancelledByUserId,
                rotation.CancellationNotes,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            throw new CrewRotationNotFoundException(assignmentId);
        }

        return new CrewRotationListItem(
            row.Id,
            row.AssignmentId,
            row.VesselId,
            row.CrewMemberId,
            row.MemberName,
            row.RankId,
            row.RankName,
            row.RelievingCrewMemberId,
            row.RelievingCrewMemberName,
            row.RotationPattern,
            row.PlannedReliefDate,
            row.PlannedReliefDate < today && row.ActualSignOffDate == null,
            row.Status,
            row.ExecutedAtUtc,
            row.ExecutedByUserId,
            row.ReplacementAssignmentId,
            row.CancelledAtUtc,
            row.CancelledByUserId,
            row.CancellationNotes);
    }
}
