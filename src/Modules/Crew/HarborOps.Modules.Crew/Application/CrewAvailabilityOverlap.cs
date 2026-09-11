using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

/// <summary>
/// Shared inclusive-boundary overlap checks for crew availability. Two periods
/// <c>[a.StartDate, a.EndDate]</c> and <c>[b.StartDate, b.EndDate]</c> overlap when
/// <c>a.StartDate &lt;= b.EndDate &amp;&amp; b.StartDate &lt;= a.EndDate</c> — touching endpoints count.
/// </summary>
internal static class CrewAvailabilityOverlap
{
    internal readonly record struct ApprovedUnavailabilityHit(DateOnly StartDate, DateOnly EndDate, string Type);

    internal static async Task EnsureNoOverlappingRequestAsync(
        DbContext dbContext,
        Guid crewMemberId,
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludeRequestId,
        CancellationToken cancellationToken)
    {
        var overlaps = await dbContext.Set<CrewAvailabilityRequest>()
            .AsNoTracking()
            .AnyAsync(
                request => request.CrewMemberId == crewMemberId
                    && request.Id != excludeRequestId
                    && (request.Status == CrewAvailabilityRequest.RequestedStatus
                        || request.Status == CrewAvailabilityRequest.ApprovedStatus)
                    && request.StartDate <= endDate
                    && startDate <= request.EndDate,
                cancellationToken);

        if (overlaps)
        {
            throw new OverlappingCrewAvailabilityRequestException(crewMemberId, startDate, endDate);
        }
    }

    internal static async Task<ApprovedUnavailabilityHit?> FindApprovedUnavailabilityAsync(
        DbContext dbContext,
        Guid crewMemberId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var hit = await dbContext.Set<CrewAvailabilityRequest>()
            .AsNoTracking()
            .Where(request => request.CrewMemberId == crewMemberId
                && request.Status == CrewAvailabilityRequest.ApprovedStatus
                && request.StartDate <= endDate
                && startDate <= request.EndDate)
            .OrderBy(request => request.StartDate)
            .ThenBy(request => request.Id)
            .Select(request => new ApprovedUnavailabilityHit(request.StartDate, request.EndDate, request.Type))
            .FirstOrDefaultAsync(cancellationToken);

        return hit.Type is null ? null : hit;
    }
}
