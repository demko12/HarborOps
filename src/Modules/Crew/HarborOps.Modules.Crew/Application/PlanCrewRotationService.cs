using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class PlanCrewRotationService(DbContext dbContext)
{
    public async Task<PlanCrewRotationResponse> PlanAsync(
        Guid assignmentId,
        PlanCrewRotationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var crewAssignments = dbContext.Set<CrewAssignment>();
        var crewRotations = dbContext.Set<CrewRotation>();

        var assignment = await crewAssignments
            .SingleOrDefaultAsync(candidate => candidate.Id == assignmentId, cancellationToken);

        if (assignment is null)
        {
            throw new CrewAssignmentNotFoundException(assignmentId);
        }

        var errors = CrewRotationValidator.Validate(
            request.RelievingCrewMemberId,
            request.RotationPattern,
            request.PlannedReliefDate,
            assignment.SignOnDate);
        if (errors.Count > 0)
        {
            throw new CrewRotationValidationException(errors);
        }

        if (!CrewRotationEligibility.IsAssignmentEligibleForRotation(assignment))
        {
            throw new CrewAssignmentNotEligibleForRotationException(assignmentId);
        }

        var hasExistingRotation = await crewRotations
            .AsNoTracking()
            .AnyAsync(
                rotation => rotation.AssignmentId == assignmentId
                    && rotation.Status == CrewRotation.PlannedStatus,
                cancellationToken);

        if (hasExistingRotation)
        {
            throw new DuplicateCrewRotationException(assignmentId);
        }

        var relievingCrewMemberId = request.RelievingCrewMemberId;
        if (relievingCrewMemberId is { } relievingId)
        {
            await CrewRotationEligibility.EnsureRelievingCrewMemberAvailableAsync(
                dbContext, assignment.CrewMemberId, relievingId, cancellationToken);
        }

        var rotation = CrewRotation.Create(
            assignmentId,
            relievingCrewMemberId,
            request.RotationPattern!.Trim(),
            request.PlannedReliefDate!.Value);

        await crewRotations.AddAsync(rotation, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PlanCrewRotationResponse(rotation.Id);
    }
}
