using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetCrewMemberAvailabilityQuery(DbContext dbContext, IDateTimeProvider dateTimeProvider)
    : IGetCrewMemberAvailabilityQuery
{
    internal const int LookbackDays = 180;
    internal const int LookaheadDays = 365;

    public async Task<GetCrewMemberAvailabilityResponse> GetAsync(
        Guid crewMemberId,
        CancellationToken cancellationToken)
    {
        var crewMemberExists = await dbContext.Set<CrewMember>()
            .AsNoTracking()
            .AnyAsync(member => member.Id == crewMemberId, cancellationToken);

        if (!crewMemberExists)
        {
            throw new CrewMemberNotFoundException(crewMemberId);
        }

        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow);
        var windowStart = today.AddDays(-LookbackDays);
        var windowEnd = today.AddDays(LookaheadDays);

        var requests = await dbContext.Set<CrewAvailabilityRequest>()
            .AsNoTracking()
            .Where(request => request.CrewMemberId == crewMemberId
                && request.EndDate >= windowStart
                && request.StartDate <= windowEnd)
            .OrderByDescending(request => request.StartDate)
            .ThenByDescending(request => request.Id)
            .Select(request => new
            {
                request.Id,
                request.Type,
                request.Status,
                request.StartDate,
                request.EndDate,
                request.Reason,
                request.Notes,
                request.RequestedAtUtc,
                request.ReviewedAtUtc,
                request.DecisionReason,
            })
            .ToListAsync(cancellationToken);

        var requestIds = requests.Select(request => request.Id).ToList();

        var transitions = await dbContext.Set<CrewAvailabilityTransition>()
            .AsNoTracking()
            .Where(transition => requestIds.Contains(transition.AvailabilityRequestId))
            .OrderByDescending(transition => transition.OccurredAtUtc)
            .ThenByDescending(transition => transition.Id)
            .Select(transition => new CrewAvailabilityTransitionItemWithParent(
                transition.AvailabilityRequestId,
                new CrewAvailabilityTransitionItem(
                    transition.Id,
                    transition.EventType,
                    transition.Reason,
                    transition.OccurredAtUtc,
                    transition.PreviousStatus,
                    transition.NewStatus)))
            .ToListAsync(cancellationToken);

        var historyByRequest = transitions
            .GroupBy(entry => entry.AvailabilityRequestId)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Item).ToList());

        var items = requests
            .Select(request => new CrewMemberAvailabilityItem(
                request.Id,
                request.Type,
                request.Status,
                request.StartDate,
                request.EndDate,
                request.Reason,
                request.Notes,
                request.RequestedAtUtc,
                request.ReviewedAtUtc,
                request.DecisionReason,
                historyByRequest.TryGetValue(request.Id, out var history)
                    ? history
                    : []))
            .ToList();

        return new GetCrewMemberAvailabilityResponse(items);
    }

    private sealed record CrewAvailabilityTransitionItemWithParent(Guid AvailabilityRequestId, CrewAvailabilityTransitionItem Item);
}
