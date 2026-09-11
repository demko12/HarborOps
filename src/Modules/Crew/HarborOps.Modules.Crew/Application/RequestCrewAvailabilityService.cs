using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class RequestCrewAvailabilityService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CreateCrewAvailabilityRequestResponse> RequestAsync(
        Guid crewMemberId,
        Guid requestedByUserId,
        CreateCrewAvailabilityRequestRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RequestCrewAvailabilityValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new CreateCrewAvailabilityRequestValidationException(errors);
        }

        var crewMemberExists = await dbContext.Set<CrewMember>()
            .AsNoTracking()
            .AnyAsync(member => member.Id == crewMemberId, cancellationToken);

        if (!crewMemberExists)
        {
            throw new CrewMemberNotFoundException(crewMemberId);
        }

        var type = request.Type!.Trim();
        var startDate = request.StartDate!.Value;
        var endDate = request.EndDate!.Value;
        var reason = request.Reason!.Trim();
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var requestedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await CrewAvailabilityOverlap.EnsureNoOverlappingRequestAsync(
            dbContext, crewMemberId, startDate, endDate, excludeRequestId: null, cancellationToken);

        var availabilityRequest = CrewAvailabilityRequest.Create(
            crewMemberId, type, startDate, endDate, reason, notes, requestedByUserId, requestedAtUtc);

        var transition = CrewAvailabilityTransition.RecordRequested(
            availabilityRequest.Id, requestedByUserId, requestedAtUtc);

        await dbContext.Set<CrewAvailabilityRequest>().AddAsync(availabilityRequest, cancellationToken);
        await dbContext.Set<CrewAvailabilityTransition>().AddAsync(transition, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateCrewAvailabilityRequestResponse(availabilityRequest.Id);
    }
}
