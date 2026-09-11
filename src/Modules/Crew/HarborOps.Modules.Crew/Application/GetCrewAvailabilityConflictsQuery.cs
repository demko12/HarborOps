using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetCrewAvailabilityConflictsQuery(DbContext dbContext) : IGetCrewAvailabilityConflictsQuery
{
    private const string ApprovedUnavailabilityRecord = "ApprovedUnavailability";
    private const string PlannedAssignmentRecord = "PlannedAssignment";
    private const string ActiveAssignmentRecord = "ActiveAssignment";
    private const string PlannedReliefRotationRecord = "PlannedReliefRotation";

    public async Task<GetCrewAvailabilityConflictsResponse> GetAsync(
        GetCrewAvailabilityConflictsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetCrewAvailabilityConflictsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetCrewAvailabilityConflictsValidationException(errors);
        }

        var crewMemberId = request.CrewMemberId!.Value;
        var from = request.From!.Value;
        var to = request.To!.Value;

        var unavailability = await dbContext.Set<CrewAvailabilityRequest>()
            .AsNoTracking()
            .Where(availability => availability.CrewMemberId == crewMemberId
                && availability.Status == CrewAvailabilityRequest.ApprovedStatus
                && availability.StartDate <= to
                && from <= availability.EndDate)
            .Select(availability => new CrewAvailabilityConflict(
                ApprovedUnavailabilityRecord,
                availability.Status,
                availability.StartDate,
                availability.EndDate,
                availability.Type))
            .ToListAsync(cancellationToken);

        var assignments = await dbContext.Set<CrewAssignment>()
            .AsNoTracking()
            .Where(assignment => assignment.CrewMemberId == crewMemberId
                && assignment.ActualSignOffDate == null
                && (assignment.Status == CrewAssignment.PlannedStatus || assignment.Status == CrewAssignment.ActiveStatus)
                && assignment.SignOnDate <= to
                && from <= assignment.PlannedSignOffDate)
            .Select(assignment => new CrewAvailabilityConflict(
                assignment.Status == CrewAssignment.ActiveStatus ? ActiveAssignmentRecord : PlannedAssignmentRecord,
                assignment.Status,
                assignment.SignOnDate,
                assignment.PlannedSignOffDate,
                null))
            .ToListAsync(cancellationToken);

        var reliefRotations = await dbContext.Set<CrewRotation>()
            .AsNoTracking()
            .Where(rotation => rotation.RelievingCrewMemberId == crewMemberId
                && rotation.Status == CrewRotation.PlannedStatus
                && rotation.PlannedReliefDate >= from
                && rotation.PlannedReliefDate <= to)
            .Select(rotation => new CrewAvailabilityConflict(
                PlannedReliefRotationRecord,
                rotation.Status,
                rotation.PlannedReliefDate,
                rotation.PlannedReliefDate,
                null))
            .ToListAsync(cancellationToken);

        var conflicts = unavailability
            .Concat(assignments)
            .Concat(reliefRotations)
            .OrderBy(conflict => conflict.StartDate)
            .ThenBy(conflict => conflict.RecordType, StringComparer.Ordinal)
            .ToList();

        return new GetCrewAvailabilityConflictsResponse(conflicts);
    }
}
