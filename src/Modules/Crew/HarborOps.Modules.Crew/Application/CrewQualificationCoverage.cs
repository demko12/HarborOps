using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

/// <summary>
/// Reusable per-day certificate coverage engine shared by the standalone eligibility query and the
/// manning-coverage extension. Checking each day independently against every matching "Valid"
/// certificate (rather than merging intervals) naturally satisfies "successive renewals may provide
/// continuous coverage" while still surfacing a genuine one-day gap.
/// </summary>
internal static class CrewQualificationCoverage
{
    private const string ValidCertificateStatus = "Valid";

    internal readonly record struct UncoveredRange(string RequiredType, DateOnly StartDate, DateOnly EndDate);

    internal sealed record EvaluationResult(
        bool IsFullyCovered,
        IReadOnlyList<string> MissingTypes,
        IReadOnlyList<UncoveredRange> UncoveredRanges,
        IReadOnlyList<Guid> ContributingCertificateIds);

    internal static async Task<EvaluationResult> EvaluateAsync(
        DbContext dbContext,
        Guid crewMemberId,
        IReadOnlyList<string> requiredTypes,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (requiredTypes.Count == 0)
        {
            return new EvaluationResult(true, [], [], []);
        }

        var certificates = await dbContext.Set<CrewCertificate>()
            .AsNoTracking()
            .Where(certificate => certificate.CrewMemberId == crewMemberId
                && certificate.Status == ValidCertificateStatus
                && certificate.RevokedAtUtc == null
                && requiredTypes.Contains(certificate.Type))
            .ToListAsync(cancellationToken);

        return Evaluate(certificates, requiredTypes, from, to);
    }

    internal static EvaluationResult Evaluate(
        IReadOnlyList<CrewCertificate> certificates,
        IReadOnlyList<string> requiredTypes,
        DateOnly from,
        DateOnly to)
    {
        var dayCount = to.DayNumber - from.DayNumber + 1;
        var dates = Enumerable.Range(0, dayCount).Select(offset => from.AddDays(offset)).ToList();

        var missingTypes = new HashSet<string>(StringComparer.Ordinal);
        var uncoveredByTypeAndDate = new List<(string Type, DateOnly Date)>();
        var contributingCertificateIds = new HashSet<Guid>();

        foreach (var type in requiredTypes)
        {
            var certificatesOfType = certificates
                .Where(certificate => certificate.Type == type
                    && certificate.Status == ValidCertificateStatus
                    && !certificate.IsRevoked)
                .ToList();

            foreach (var date in dates)
            {
                var coveringCertificate = certificatesOfType.FirstOrDefault(
                    certificate => certificate.IssueDate <= date && date <= certificate.ExpiryDate);

                if (coveringCertificate is null)
                {
                    missingTypes.Add(type);
                    uncoveredByTypeAndDate.Add((type, date));
                }
                else
                {
                    contributingCertificateIds.Add(coveringCertificate.Id);
                }
            }
        }

        return new EvaluationResult(
            uncoveredByTypeAndDate.Count == 0,
            missingTypes.OrderBy(type => type, StringComparer.Ordinal).ToList(),
            CollapseUncoveredRanges(uncoveredByTypeAndDate),
            contributingCertificateIds.ToList());
    }

    internal static bool IsCoveredOnDay(IReadOnlyList<CrewCertificate> certificates, string requiredType, DateOnly date) =>
        certificates.Any(certificate => certificate.Type == requiredType
            && certificate.Status == ValidCertificateStatus
            && !certificate.IsRevoked
            && certificate.IssueDate <= date
            && date <= certificate.ExpiryDate);

    private static List<UncoveredRange> CollapseUncoveredRanges(List<(string Type, DateOnly Date)> uncovered)
    {
        var ranges = new List<UncoveredRange>();

        foreach (var group in uncovered.GroupBy(entry => entry.Type))
        {
            var orderedDates = group.Select(entry => entry.Date).OrderBy(date => date).ToList();

            DateOnly? start = null;
            DateOnly? end = null;

            foreach (var date in orderedDates)
            {
                if (start is null)
                {
                    start = date;
                    end = date;
                    continue;
                }

                if (date.DayNumber == end!.Value.DayNumber + 1)
                {
                    end = date;
                    continue;
                }

                ranges.Add(new UncoveredRange(group.Key, start.Value, end.Value));
                start = date;
                end = date;
            }

            if (start is not null)
            {
                ranges.Add(new UncoveredRange(group.Key, start.Value, end!.Value));
            }
        }

        return ranges.OrderBy(range => range.StartDate).ThenBy(range => range.RequiredType, StringComparer.Ordinal).ToList();
    }
}
