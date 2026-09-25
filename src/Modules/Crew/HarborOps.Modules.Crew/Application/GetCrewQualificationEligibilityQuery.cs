using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetCrewQualificationEligibilityQuery(DbContext dbContext) : IGetCrewQualificationEligibilityQuery
{
    public async Task<GetCrewQualificationEligibilityResponse> GetAsync(
        GetCrewQualificationEligibilityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetCrewQualificationEligibilityValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetCrewQualificationEligibilityValidationException(errors);
        }

        var crewMemberId = request.CrewMemberId!.Value;
        var rankId = request.RankId!.Value;
        var from = request.From!.Value;
        var to = request.To!.Value;

        var memberExists = await dbContext.Set<CrewMember>()
            .AsNoTracking()
            .AnyAsync(member => member.Id == crewMemberId, cancellationToken);

        if (!memberExists)
        {
            throw new CrewMemberNotFoundException(crewMemberId);
        }

        var policy = await dbContext.Set<CrewQualificationPolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.CrewPositionId == rankId, cancellationToken);

        if (policy is null || !policy.Enabled)
        {
            return new GetCrewQualificationEligibilityResponse(
                crewMemberId, rankId, from, to, QualificationStatuses.NotConfigured, [], [], []);
        }

        var requiredTypes = await dbContext.Set<CrewQualificationRequirement>()
            .AsNoTracking()
            .Where(requirement => requirement.PolicyId == policy.Id)
            .Select(requirement => requirement.RequiredType)
            .ToListAsync(cancellationToken);

        var result = await CrewQualificationCoverage.EvaluateAsync(
            dbContext, crewMemberId, requiredTypes, from, to, cancellationToken);

        var status = result.IsFullyCovered ? QualificationStatuses.Qualified : QualificationStatuses.NotQualified;

        var uncoveredDates = result.UncoveredRanges
            .Select(range => new QualificationUncoveredRange(range.RequiredType, range.StartDate, range.EndDate))
            .ToList();

        return new GetCrewQualificationEligibilityResponse(
            crewMemberId, rankId, from, to, status, result.MissingTypes, uncoveredDates, result.ContributingCertificateIds);
    }
}
