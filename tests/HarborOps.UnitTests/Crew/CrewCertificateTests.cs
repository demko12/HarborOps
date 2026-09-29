using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class CrewCertificateTests
{
    private static readonly DateTime RevokedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_sets_no_predecessor_and_is_not_revoked()
    {
        var certificate = CrewCertificate.Create(
            Guid.NewGuid(), "STCW", "Basic Safety", "Authority", "LIC-0001",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "Valid");

        Assert.Null(certificate.PredecessorCertificateId);
        Assert.False(certificate.IsRevoked);
        Assert.NotEqual(Guid.Empty, certificate.ConcurrencyToken);
    }

    [Fact]
    public void CreateRenewal_links_the_predecessor_and_carries_member_and_type()
    {
        var memberId = Guid.NewGuid();
        var predecessor = CrewCertificate.Create(
            memberId, "STCW", "Basic Safety", "Authority", "LIC-0001",
            new DateOnly(2020, 1, 1), new DateOnly(2024, 1, 1), "Expired");

        var successor = CrewCertificate.CreateRenewal(
            predecessor.Id, memberId, predecessor.Type, "Basic Safety", "Authority", "LIC-0002",
            new DateOnly(2024, 1, 1), new DateOnly(2028, 1, 1), "Valid");

        Assert.Equal(predecessor.Id, successor.PredecessorCertificateId);
        Assert.Equal(memberId, successor.CrewMemberId);
        Assert.Equal(predecessor.Type, successor.Type);
        Assert.False(successor.IsRevoked);
        Assert.NotEqual(predecessor.Id, successor.Id);
    }

    [Fact]
    public void Revoke_sets_lifecycle_fields_and_bumps_the_concurrency_token()
    {
        var certificate = CrewCertificate.Create(
            Guid.NewGuid(), "STCW", "Basic Safety", "Authority", "LIC-0001",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "Valid");
        var previousToken = certificate.ConcurrencyToken;
        var actorUserId = Guid.NewGuid();

        certificate.Revoke(actorUserId, "Issued in error", RevokedAtUtc);

        Assert.True(certificate.IsRevoked);
        Assert.Equal(RevokedAtUtc, certificate.RevokedAtUtc);
        Assert.Equal(actorUserId, certificate.RevokedByUserId);
        Assert.Equal("Issued in error", certificate.RevocationReason);
        Assert.NotEqual(previousToken, certificate.ConcurrencyToken);
    }

    [Fact]
    public void Revoke_guards_double_revocation()
    {
        var certificate = CrewCertificate.Create(
            Guid.NewGuid(), "STCW", "Basic Safety", "Authority", "LIC-0001",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "Valid");
        certificate.Revoke(Guid.NewGuid(), "Issued in error", RevokedAtUtc);

        var exception = Assert.Throws<CrewCertificateAlreadyRevokedException>(
            () => certificate.Revoke(Guid.NewGuid(), "Second attempt", RevokedAtUtc.AddDays(1)));

        Assert.Equal(certificate.Id, exception.CertificateId);
    }
}
