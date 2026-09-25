using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetCrewManningCoverageQuery(DbContext dbContext) : IGetCrewManningCoverageQuery
{
    internal const string ApprovedUnavailabilityReason = "ApprovedUnavailability";
    internal const string InactiveEmploymentReason = "InactiveEmployment";

    public async Task<GetCrewManningCoverageResponse> GetAsync(
        GetCrewManningCoverageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetCrewManningCoverageValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetCrewManningCoverageValidationException(errors);
        }

        var vesselId = request.VesselId!.Value;
        var windowFrom = request.From!.Value;
        var windowTo = request.To!.Value;

        var dayCount = windowTo.DayNumber - windowFrom.DayNumber + 1;
        var dates = Enumerable.Range(0, dayCount).Select(offset => windowFrom.AddDays(offset)).ToList();

        var planLines = await dbContext.Set<VesselManningPlan>()
            .AsNoTracking()
            .Where(plan => plan.VesselId == vesselId
                && plan.EffectiveStartDate <= windowTo
                && windowFrom <= plan.EffectiveEndDate)
            .Join(
                dbContext.Set<VesselManningPlanLine>().AsNoTracking(),
                plan => plan.Id,
                line => line.VesselManningPlanId,
                (plan, line) => new
                {
                    line.RankId,
                    line.RequiredHeadcount,
                    plan.EffectiveStartDate,
                    plan.EffectiveEndDate,
                })
            .ToListAsync(cancellationToken);

        var vesselAssignments = await dbContext.Set<CrewAssignment>()
            .AsNoTracking()
            .Where(assignment => assignment.VesselId == vesselId
                && assignment.Status != CrewAssignment.CancelledStatus)
            .ToListAsync(cancellationToken);

        var contributingAssignments = vesselAssignments
            .Select(assignment => new
            {
                Assignment = assignment,
                ContributionEnd = assignment.Status == CrewAssignment.CompletedStatus
                    ? assignment.ActualSignOffDate!.Value
                    : assignment.PlannedSignOffDate,
            })
            .Where(entry => entry.Assignment.SignOnDate <= windowTo && windowFrom <= entry.ContributionEnd)
            .ToList();

        var rankIds = planLines.Select(line => line.RankId)
            .Concat(contributingAssignments.Select(entry => entry.Assignment.RankId))
            .Distinct()
            .ToList();

        var rankNames = await dbContext.Set<CrewPosition>()
            .AsNoTracking()
            .Where(position => rankIds.Contains(position.Id))
            .ToDictionaryAsync(position => position.Id, position => position.RankName, cancellationToken);

        var memberIds = contributingAssignments.Select(entry => entry.Assignment.CrewMemberId).Distinct().ToList();

        var employmentStatuses = await dbContext.Set<CrewMember>()
            .AsNoTracking()
            .Where(member => memberIds.Contains(member.Id))
            .ToDictionaryAsync(member => member.Id, member => member.EmploymentStatus, cancellationToken);

        var approvedUnavailability = await dbContext.Set<CrewAvailabilityRequest>()
            .AsNoTracking()
            .Where(availability => memberIds.Contains(availability.CrewMemberId)
                && availability.Status == CrewAvailabilityRequest.ApprovedStatus
                && availability.StartDate <= windowTo
                && windowFrom <= availability.EndDate)
            .Select(availability => new { availability.CrewMemberId, availability.StartDate, availability.EndDate })
            .ToListAsync(cancellationToken);

        var enabledPolicies = await dbContext.Set<CrewQualificationPolicy>()
            .AsNoTracking()
            .Where(policy => rankIds.Contains(policy.CrewPositionId) && policy.Enabled)
            .ToDictionaryAsync(policy => policy.CrewPositionId, cancellationToken);

        var policyIds = enabledPolicies.Values.Select(policy => policy.Id).ToList();

        var requiredTypesByPolicyId = (await dbContext.Set<CrewQualificationRequirement>()
            .AsNoTracking()
            .Where(requirement => policyIds.Contains(requirement.PolicyId))
            .ToListAsync(cancellationToken))
            .GroupBy(requirement => requirement.PolicyId)
            .ToDictionary(group => group.Key, group => group.Select(requirement => requirement.RequiredType).ToList());

        var validCertificatesByMemberId = (await dbContext.Set<CrewCertificate>()
            .AsNoTracking()
            .Where(certificate => memberIds.Contains(certificate.CrewMemberId) && certificate.Status == "Valid")
            .ToListAsync(cancellationToken))
            .GroupBy(certificate => certificate.CrewMemberId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CrewCertificate>)group.ToList());

        var days = new List<CoverageDayItem>();

        foreach (var date in dates)
        {
            var planLinesForDate = planLines
                .Where(line => line.EffectiveStartDate <= date && date <= line.EffectiveEndDate)
                .ToList();

            var assignmentsForDate = contributingAssignments
                .Where(entry => entry.Assignment.SignOnDate <= date && date <= entry.ContributionEnd)
                .ToList();

            var ranksForDate = planLinesForDate.Select(line => line.RankId)
                .Concat(assignmentsForDate.Select(entry => entry.Assignment.RankId))
                .Distinct();

            foreach (var rankId in ranksForDate)
            {
                var requiredHeadcount = planLinesForDate
                    .Where(line => line.RankId == rankId)
                    .Select(line => (int?)line.RequiredHeadcount)
                    .FirstOrDefault();

                var assignmentsForRank = assignmentsForDate
                    .Where(entry => entry.Assignment.RankId == rankId)
                    .ToList();

                var distinctMemberIds = assignmentsForRank
                    .Select(entry => entry.Assignment.CrewMemberId)
                    .Distinct()
                    .ToList();

                var ineligibleMembers = new List<CoverageIneligibleMember>();
                var eligibleMemberIds = new List<Guid>();

                foreach (var memberId in distinctMemberIds)
                {
                    if (employmentStatuses.TryGetValue(memberId, out var status)
                        && status != CrewEmploymentStatuses.Active)
                    {
                        ineligibleMembers.Add(new CoverageIneligibleMember(memberId, InactiveEmploymentReason));
                        continue;
                    }

                    var isUnavailable = approvedUnavailability.Any(
                        availability => availability.CrewMemberId == memberId
                            && availability.StartDate <= date
                            && date <= availability.EndDate);

                    if (isUnavailable)
                    {
                        ineligibleMembers.Add(new CoverageIneligibleMember(memberId, ApprovedUnavailabilityReason));
                        continue;
                    }

                    eligibleMemberIds.Add(memberId);
                }

                var eligibleHeadcount = eligibleMemberIds.Count;

                int? qualifiedHeadcount = null;
                var unqualifiedMembers = new List<CoverageUnqualifiedMember>();

                if (enabledPolicies.TryGetValue(rankId, out var policy))
                {
                    var requiredTypes = requiredTypesByPolicyId.GetValueOrDefault(policy.Id, []);
                    var qualifiedCount = 0;

                    foreach (var memberId in eligibleMemberIds)
                    {
                        var certificates = validCertificatesByMemberId.GetValueOrDefault(memberId, []);
                        var missingTypes = requiredTypes
                            .Where(type => !CrewQualificationCoverage.IsCoveredOnDay(certificates, type, date))
                            .ToList();

                        if (missingTypes.Count == 0)
                        {
                            qualifiedCount++;
                        }
                        else
                        {
                            unqualifiedMembers.Add(new CoverageUnqualifiedMember(memberId, missingTypes));
                        }
                    }

                    qualifiedHeadcount = qualifiedCount;
                }

                var shortage = requiredHeadcount is null
                    ? (int?)null
                    : Math.Max(0, requiredHeadcount.Value - eligibleHeadcount);

                days.Add(new CoverageDayItem(
                    date,
                    rankId,
                    rankNames.GetValueOrDefault(rankId, string.Empty),
                    requiredHeadcount,
                    distinctMemberIds.Count,
                    eligibleHeadcount,
                    shortage,
                    ineligibleMembers,
                    assignmentsForRank.Select(entry => entry.Assignment.Id).ToList(),
                    qualifiedHeadcount,
                    unqualifiedMembers));
            }
        }

        var orderedDays = days.OrderBy(day => day.Date).ThenBy(day => day.RankId).ToList();
        var shortageIntervals = CollapseShortageIntervals(orderedDays);

        return new GetCrewManningCoverageResponse(vesselId, windowFrom, windowTo, orderedDays, shortageIntervals);
    }

    private static List<CoverageShortageInterval> CollapseShortageIntervals(IReadOnlyList<CoverageDayItem> days)
    {
        var intervals = new List<CoverageShortageInterval>();

        foreach (var rankGroup in days.Where(day => day.Shortage is > 0).GroupBy(day => day.RankId))
        {
            var rankDays = rankGroup.OrderBy(day => day.Date).ToList();

            CoverageDayItem? intervalStart = null;
            CoverageDayItem? intervalEnd = null;

            foreach (var day in rankDays)
            {
                if (intervalStart is null)
                {
                    intervalStart = day;
                    intervalEnd = day;
                    continue;
                }

                var isConsecutive = day.Date.DayNumber == intervalEnd!.Date.DayNumber + 1;
                var sameShortage = day.RequiredHeadcount == intervalEnd.RequiredHeadcount
                    && day.Shortage == intervalEnd.Shortage;

                if (isConsecutive && sameShortage)
                {
                    intervalEnd = day;
                    continue;
                }

                intervals.Add(ToInterval(rankGroup.Key, intervalStart, intervalEnd));
                intervalStart = day;
                intervalEnd = day;
            }

            if (intervalStart is not null)
            {
                intervals.Add(ToInterval(rankGroup.Key, intervalStart, intervalEnd!));
            }
        }

        return intervals.OrderBy(interval => interval.StartDate).ThenBy(interval => interval.RankId).ToList();
    }

    private static CoverageShortageInterval ToInterval(Guid rankId, CoverageDayItem start, CoverageDayItem end)
    {
        return new CoverageShortageInterval(
            rankId, start.RankName, start.Date, end.Date, start.RequiredHeadcount!.Value, start.Shortage!.Value);
    }
}
