using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class RegisterCrewAssignmentService(DbContext dbContext)
{
    public async Task<RegisterCrewAssignmentResponse> RegisterAsync(
        RegisterCrewAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterCrewAssignmentValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterCrewAssignmentValidationException(errors);
        }

        var crewMemberId = request.CrewMemberId!.Value;
        var vesselId = request.VesselId!.Value;
        var rankId = request.RankId!.Value;
        var signOnDate = request.SignOnDate!.Value;
        var plannedSignOffDate = request.PlannedSignOffDate!.Value;

        var crewMembers = dbContext.Set<CrewMember>();
        var crewPositions = dbContext.Set<CrewPosition>();
        var crewAssignments = dbContext.Set<CrewAssignment>();

        var crewMemberExists = await crewMembers
            .AsNoTracking()
            .AnyAsync(crewMember => crewMember.Id == crewMemberId, cancellationToken);

        if (!crewMemberExists)
        {
            throw new CrewMemberNotFoundException(crewMemberId);
        }

        var rankExists = await crewPositions
            .AsNoTracking()
            .AnyAsync(crewPosition => crewPosition.Id == rankId, cancellationToken);

        if (!rankExists)
        {
            throw new CrewPositionNotFoundException(rankId);
        }

        var hasOpenAssignment = await crewAssignments
            .AsNoTracking()
            .AnyAsync(
                assignment => assignment.CrewMemberId == crewMemberId
                    && assignment.ActualSignOffDate == null
                    && (assignment.Status == CrewAssignment.PlannedStatus
                        || assignment.Status == CrewAssignment.ActiveStatus),
                cancellationToken);

        if (hasOpenAssignment)
        {
            throw new OpenCrewAssignmentExistsException(crewMemberId);
        }

        var unavailability = await CrewAvailabilityOverlap.FindApprovedUnavailabilityAsync(
            dbContext, crewMemberId, signOnDate, plannedSignOffDate, cancellationToken);

        if (unavailability is { } hit)
        {
            throw new CrewMemberUnavailableException(
                crewMemberId, hit.StartDate, hit.EndDate, "ApprovedUnavailability");
        }

        var crewAssignment = CrewAssignment.CreatePlanned(
            crewMemberId,
            vesselId,
            rankId,
            request.SignOnPortId,
            signOnDate,
            plannedSignOffDate);

        await crewAssignments.AddAsync(crewAssignment, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterCrewAssignmentResponse(crewAssignment.Id);
    }
}
