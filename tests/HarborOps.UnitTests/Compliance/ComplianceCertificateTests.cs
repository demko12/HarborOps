using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.UnitTests.Compliance;

public sealed class ComplianceCertificateTests
{
    private static readonly DateTime RenewedAtUtc = new(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Renew_creates_a_linked_certificate_for_the_same_vessel_and_type_and_keeps_the_original()
    {
        var original = CreateOriginal();
        var originalToken = original.ConcurrencyToken;

        var successor = original.Renew("SMC-2027", "Flag State", new DateOnly(2026, 12, 1), new DateOnly(2031, 12, 1), RenewedAtUtc);

        Assert.NotEqual(original.Id, successor.Id);
        Assert.Equal(original.Id, successor.PredecessorCertificateId);
        Assert.Equal(original.VesselId, successor.VesselId);
        Assert.Equal(original.CertificateType, successor.CertificateType);
        Assert.Equal("SMC-2027", successor.CertificateNumber);
        Assert.Equal(RenewedAtUtc, successor.CreatedUtc);
        Assert.True(successor.IsDirectSuccessorOf(original));
        Assert.Equal("SMC-2022", original.CertificateNumber);
        Assert.Equal(new DateOnly(2027, 1, 1), original.ExpiresOn);
        Assert.False(original.IsRevoked);
        Assert.NotEqual(originalToken, original.ConcurrencyToken);
    }

    [Fact]
    public void Renew_rejects_a_revoked_certificate()
    {
        var original = CreateOriginal();
        original.Revoke(Guid.NewGuid(), "Issued in error by the flag state.", RenewedAtUtc);

        var exception = Assert.Throws<InvalidComplianceCertificateTransitionException>(
            () => original.Renew("SMC-2027", "Flag State", new DateOnly(2026, 12, 1), new DateOnly(2031, 12, 1), RenewedAtUtc));

        Assert.Equal(ComplianceCertificate.RenewAction, exception.AttemptedAction);
    }

    [Theory]
    [InlineData("2021-12-31", "2031-12-01")]
    [InlineData("2026-12-01", "2027-01-01")]
    [InlineData("2026-12-01", "2026-06-01")]
    [InlineData("2027-06-01", "2027-05-31")]
    public void Renew_rejects_dates_that_start_before_the_original_or_do_not_extend_it(string issuedOn, string expiresOn)
    {
        var original = CreateOriginal();

        Assert.Throws<InvalidComplianceCertificateTransitionException>(
            () => original.Renew("SMC-2027", "Flag State", DateOnly.Parse(issuedOn), DateOnly.Parse(expiresOn), RenewedAtUtc));
    }

    [Fact]
    public void Revoke_records_actor_time_and_reason_and_is_terminal()
    {
        var certificate = CreateOriginal();
        var actorUserId = Guid.NewGuid();
        var token = certificate.ConcurrencyToken;

        certificate.Revoke(actorUserId, "Issued in error by the flag state.", RenewedAtUtc);

        Assert.True(certificate.IsRevoked);
        Assert.Equal(RenewedAtUtc, certificate.RevokedAtUtc);
        Assert.Equal(actorUserId, certificate.RevokedByUserId);
        Assert.Equal("Issued in error by the flag state.", certificate.RevocationReason);
        Assert.NotEqual(token, certificate.ConcurrencyToken);
        Assert.Throws<InvalidComplianceCertificateTransitionException>(
            () => certificate.Revoke(actorUserId, "Second revocation attempt.", RenewedAtUtc));
    }

    [Fact]
    public void Revoking_the_original_does_not_revoke_its_successor()
    {
        var original = CreateOriginal();
        var successor = original.Renew("SMC-2027", "Flag State", new DateOnly(2026, 12, 1), new DateOnly(2031, 12, 1), RenewedAtUtc);

        original.Revoke(Guid.NewGuid(), "Original found to be forged.", RenewedAtUtc);

        Assert.True(original.IsRevoked);
        Assert.False(successor.IsRevoked);
    }

    [Fact]
    public void IsDirectSuccessorOf_rejects_links_to_another_vessel_or_type()
    {
        var original = CreateOriginal();
        var otherVessel = ComplianceCertificate.Register(Guid.NewGuid(), original.CertificateType, "X", "Flag State", new DateOnly(2022, 1, 1), new DateOnly(2027, 1, 1));
        var otherType = ComplianceCertificate.Register(original.VesselId, "Load Line", "Y", "Flag State", new DateOnly(2022, 1, 1), new DateOnly(2027, 1, 1));
        var successor = original.Renew("SMC-2027", "Flag State", new DateOnly(2026, 12, 1), new DateOnly(2031, 12, 1), RenewedAtUtc);

        Assert.False(successor.IsDirectSuccessorOf(otherVessel));
        Assert.False(successor.IsDirectSuccessorOf(otherType));
        Assert.False(original.IsDirectSuccessorOf(successor));
    }

    private static ComplianceCertificate CreateOriginal() => ComplianceCertificate.Register(
        Guid.NewGuid(),
        "Safety Management Certificate",
        "SMC-2022",
        "Flag State",
        new DateOnly(2022, 1, 1),
        new DateOnly(2027, 1, 1));
}
