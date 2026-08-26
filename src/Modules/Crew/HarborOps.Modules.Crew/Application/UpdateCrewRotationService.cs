using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class UpdateCrewRotationService(DbContext dbContext)
{
    public async Task<UpdateCrewRotationResponse> UpdateAsync(
        Guid assignmentId,
        UpdateCrewRotationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var assignment = await dbContext.Set<CrewAssignment>()
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

        var rotation = await dbContext.Set<CrewRotation>()
            .SingleOrDefaultAsync(
                candidate => candidate.AssignmentId == assignmentId
                    && candidate.Status == CrewRotation.PlannedStatus,
                cancellationToken);

        if (rotation is null)
        {
            throw new CrewRotationNotFoundException(assignmentId);
        }

        var relievingCrewMemberId = request.RelievingCrewMemberId;
        if (relievingCrewMemberId is { } relievingId)
        {
            await CrewRotationEligibility.EnsureRelievingCrewMemberAvailableAsync(
                dbContext, assignment.CrewMemberId, relievingId, cancellationToken);
        }

        rotation.Update(relievingCrewMemberId, request.RotationPattern!.Trim(), request.PlannedReliefDate!.Value);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateCrewRotationResponse(rotation.Id);
    }
}
