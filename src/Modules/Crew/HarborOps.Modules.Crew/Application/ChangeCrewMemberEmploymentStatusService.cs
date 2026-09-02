using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class ChangeCrewMemberEmploymentStatusService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ChangeCrewMemberEmploymentStatusResponse> ChangeAsync(
        Guid crewMemberId,
        Guid actingUserId,
        ChangeCrewMemberEmploymentStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ChangeCrewMemberEmploymentStatusValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ChangeCrewMemberEmploymentStatusValidationException(errors);
        }

        var crewMember = await dbContext.Set<CrewMember>()
            .SingleOrDefaultAsync(candidate => candidate.Id == crewMemberId, cancellationToken)
            ?? throw new CrewMemberNotFoundException(crewMemberId);

        var status = request.Status!.Trim();

        if (string.Equals(crewMember.EmploymentStatus, status, StringComparison.Ordinal))
        {
            throw new CrewMemberEmploymentStatusUnchangedException(crewMemberId, status);
        }

        if (status == CrewEmploymentStatuses.Terminated)
        {
            var hasOpenAssignment = await dbContext.Set<CrewAssignment>()
                .AsNoTracking()
                .AnyAsync(
                    assignment => assignment.CrewMemberId == crewMemberId
                        && assignment.ActualSignOffDate == null
                        && (assignment.Status == CrewAssignment.PlannedStatus
                            || assignment.Status == CrewAssignment.ActiveStatus),
                    cancellationToken);

            var hasOpenRotation = await (
                from rotation in dbContext.Set<CrewRotation>().AsNoTracking()
                join assignment in dbContext.Set<CrewAssignment>().AsNoTracking()
                    on rotation.AssignmentId equals assignment.Id
                where assignment.CrewMemberId == crewMemberId
                    && rotation.Status == CrewRotation.PlannedStatus
                select rotation.Id)
                .AnyAsync(cancellationToken);

            if (hasOpenAssignment || hasOpenRotation)
            {
                throw new CrewMemberHasOpenAssignmentOrRotationException(crewMemberId);
            }
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var previousStatus = crewMember.EmploymentStatus;
        var changedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        crewMember.ChangeEmploymentStatus(status);

        var history = CrewEmploymentStatusHistory.Record(crewMemberId, status, actingUserId, notes, changedAtUtc);
        await dbContext.Set<CrewEmploymentStatusHistory>().AddAsync(history, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ChangeCrewMemberEmploymentStatusResponse(
            history.Id,
            crewMemberId,
            previousStatus,
            status,
            actingUserId,
            notes,
            changedAtUtc);
    }
}
