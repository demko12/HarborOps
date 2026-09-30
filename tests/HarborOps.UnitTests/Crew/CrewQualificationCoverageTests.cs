using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class CrewQualificationCoverageTests
{
    private static readonly Guid MemberId = Guid.NewGuid();

    [Fact]
    public void Evaluate_returns_fully_covered_when_zero_required_types()
    {
        var result = CrewQualificationCoverage.Evaluate([], [], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));

        Assert.True(result.IsFullyCovered);
        Assert.Empty(result.MissingTypes);
        Assert.Empty(result.UncoveredRanges);
    }

    [Fact]
    public void Evaluate_covers_the_full_window_when_one_certificate_spans_it_with_inclusive_boundaries()
    {
        var certificate = Certificate("STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));

        var result = CrewQualificationCoverage.Evaluate(
            [certificate], ["STCW-II/1"], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));

        Assert.True(result.IsFullyCovered);
        Assert.Contains(certificate.Id, result.ContributingCertificateIds);
    }

    [Fact]
    public void Evaluate_treats_back_to_back_renewals_with_no_gap_as_fully_covered()
    {
        var first = Certificate("STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        var second = Certificate("STCW-II/1", new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31));

        var result = CrewQualificationCoverage.Evaluate(
            [first, second], ["STCW-II/1"], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.True(result.IsFullyCovered);
        Assert.Contains(first.Id, result.ContributingCertificateIds);
        Assert.Contains(second.Id, result.ContributingCertificateIds);
    }

    [Fact]
    public void Evaluate_detects_an_exact_one_day_gap_between_renewals()
    {
        var first = Certificate("STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        var second = Certificate("STCW-II/1", new DateOnly(2026, 7, 2), new DateOnly(2026, 12, 31));

        var result = CrewQualificationCoverage.Evaluate(
            [first, second], ["STCW-II/1"], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.False(result.IsFullyCovered);
        Assert.Contains("STCW-II/1", result.MissingTypes);
        var range = Assert.Single(result.UncoveredRanges);
        Assert.Equal(new DateOnly(2026, 7, 1), range.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 1), range.EndDate);
    }

    [Fact]
    public void Evaluate_collapses_consecutive_uncovered_days_into_one_range()
    {
        var result = CrewQualificationCoverage.Evaluate(
            [], ["STCW-II/1"], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

        Assert.False(result.IsFullyCovered);
        var range = Assert.Single(result.UncoveredRanges);
        Assert.Equal(new DateOnly(2026, 1, 1), range.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 5), range.EndDate);
    }

    [Fact]
    public void Evaluate_excludes_a_future_dated_certificate_from_covering_earlier_days()
    {
        var certificate = Certificate("STCW-II/1", new DateOnly(2026, 6, 1), new DateOnly(2026, 12, 31));

        var result = CrewQualificationCoverage.Evaluate(
            [certificate], ["STCW-II/1"], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));

        Assert.False(result.IsFullyCovered);
        Assert.Equal(10, result.UncoveredRanges.Single().EndDate.DayNumber - result.UncoveredRanges.Single().StartDate.DayNumber + 1);
    }

    [Fact]
    public void Evaluate_ignores_a_certificate_whose_date_range_covers_the_window_but_status_is_not_valid()
    {
        var certificate = Certificate("STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), status: "Expired");

        var result = CrewQualificationCoverage.Evaluate(
            [certificate], ["STCW-II/1"], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));

        Assert.False(result.IsFullyCovered);
        Assert.DoesNotContain(certificate.Id, result.ContributingCertificateIds);
    }

    [Fact]
    public void Evaluate_excludes_a_revoked_certificate_whose_date_range_would_otherwise_cover_the_window()
    {
        var revoked = Certificate("STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        revoked.Revoke(Guid.NewGuid(), "Issued in error", new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));

        var result = CrewQualificationCoverage.Evaluate(
            [revoked], ["STCW-II/1"], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));

        Assert.False(result.IsFullyCovered);
        Assert.DoesNotContain(revoked.Id, result.ContributingCertificateIds);
    }

    [Fact]
    public void Evaluate_still_covers_via_a_non_revoked_successor_when_the_predecessor_is_revoked()
    {
        var predecessor = Certificate("STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        predecessor.Revoke(Guid.NewGuid(), "Superseded", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var successor = CrewCertificate.CreateRenewal(
            predecessor.Id, MemberId, "STCW-II/1", "STCW-II/1 certificate", "Flag Authority",
            $"LIC-{Guid.NewGuid():N}"[..15], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "Valid");

        var result = CrewQualificationCoverage.Evaluate(
            [predecessor, successor], ["STCW-II/1"], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.True(result.IsFullyCovered);
        Assert.Contains(successor.Id, result.ContributingCertificateIds);
        Assert.DoesNotContain(predecessor.Id, result.ContributingCertificateIds);
    }

    [Fact]
    public void IsCoveredOnDay_excludes_a_revoked_certificate()
    {
        var certificate = Certificate("STCW-II/1", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 10));
        certificate.Revoke(Guid.NewGuid(), "Issued in error", new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc));

        Assert.False(CrewQualificationCoverage.IsCoveredOnDay([certificate], "STCW-II/1", new DateOnly(2026, 1, 7)));
    }

    [Fact]
    public void Evaluate_reports_missing_types_independently_per_required_type()
    {
        var covered = Certificate("STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));

        var result = CrewQualificationCoverage.Evaluate(
            [covered], ["STCW-II/1", "STCW-VI/1"], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));

        Assert.False(result.IsFullyCovered);
        Assert.Equal(["STCW-VI/1"], result.MissingTypes);
        Assert.All(result.UncoveredRanges, range => Assert.Equal("STCW-VI/1", range.RequiredType));
    }

    [Fact]
    public void IsCoveredOnDay_matches_only_valid_status_and_the_inclusive_date_range()
    {
        var certificate = Certificate("STCW-II/1", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 10));

        Assert.True(CrewQualificationCoverage.IsCoveredOnDay([certificate], "STCW-II/1", new DateOnly(2026, 1, 5)));
        Assert.True(CrewQualificationCoverage.IsCoveredOnDay([certificate], "STCW-II/1", new DateOnly(2026, 1, 10)));
        Assert.False(CrewQualificationCoverage.IsCoveredOnDay([certificate], "STCW-II/1", new DateOnly(2026, 1, 4)));
        Assert.False(CrewQualificationCoverage.IsCoveredOnDay([certificate], "STCW-II/1", new DateOnly(2026, 1, 11)));
        Assert.False(CrewQualificationCoverage.IsCoveredOnDay([certificate], "STCW-VI/1", new DateOnly(2026, 1, 7)));
    }

    private static CrewCertificate Certificate(
        string type, DateOnly issueDate, DateOnly expiryDate, string status = "Valid") =>
        CrewCertificate.Create(MemberId, type, $"{type} certificate", "Flag Authority", $"LIC-{Guid.NewGuid():N}"[..15], issueDate, expiryDate, status);
}
