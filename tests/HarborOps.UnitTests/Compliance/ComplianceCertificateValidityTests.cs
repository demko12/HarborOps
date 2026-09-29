using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.UnitTests.Compliance;

public sealed class ComplianceCertificateValidityTests
{
    private static readonly DateOnly IssuedOn = new(2026, 1, 1);
    private static readonly DateOnly ExpiresOn = new(2026, 12, 31);

    [Theory]
    [InlineData("2025-12-31", ComplianceCertificate.NotYetEffectiveStatus)]
    [InlineData("2026-01-01", ComplianceCertificate.CurrentStatus)]
    [InlineData("2026-06-15", ComplianceCertificate.CurrentStatus)]
    [InlineData("2026-12-31", ComplianceCertificate.CurrentStatus)]
    [InlineData("2027-01-01", ComplianceCertificate.ExpiredStatus)]
    public void Status_treats_issue_and_expiry_dates_as_inclusive(string asOf, string expected)
    {
        Assert.Equal(expected, ComplianceCertificateValidity.Status(IssuedOn, ExpiresOn, isRevoked: false, DateOnly.Parse(asOf)));
    }

    [Theory]
    [InlineData("2025-12-31")]
    [InlineData("2026-06-15")]
    [InlineData("2027-01-01")]
    public void Status_reports_revoked_regardless_of_dates(string asOf)
    {
        Assert.Equal(
            ComplianceCertificate.RevokedStatus,
            ComplianceCertificateValidity.Status(IssuedOn, ExpiresOn, isRevoked: true, DateOnly.Parse(asOf)));
    }

    [Theory]
    [InlineData("2026-06-01", "2031-06-01", false, true)]
    [InlineData("2026-12-31", "2031-12-31", false, true)]
    [InlineData("2027-01-01", "2032-01-01", false, true)]
    [InlineData("2027-01-02", "2032-01-01", false, false)]
    [InlineData("2026-06-01", "2026-12-31", false, false)]
    [InlineData("2027-01-01", "2032-01-01", true, false)]
    public void CoversExpiry_requires_a_non_revoked_successor_without_a_gap_that_extends_validity(
        string successorIssuedOn,
        string successorExpiresOn,
        bool successorIsRevoked,
        bool expected)
    {
        Assert.Equal(
            expected,
            ComplianceCertificateValidity.CoversExpiry(
                ExpiresOn,
                DateOnly.Parse(successorIssuedOn),
                DateOnly.Parse(successorExpiresOn),
                successorIsRevoked));
    }

    [Theory]
    [InlineData("2026-06-01", 0)]
    [InlineData("2027-01-01", 0)]
    [InlineData("2027-01-02", 1)]
    [InlineData("2027-01-31", 30)]
    public void GapDays_counts_uncovered_days_between_expiry_and_the_successor_issue_date(string successorIssuedOn, int expected)
    {
        Assert.Equal(expected, ComplianceCertificateValidity.GapDays(ExpiresOn, DateOnly.Parse(successorIssuedOn)));
    }
}
