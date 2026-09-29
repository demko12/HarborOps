using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class ExpiryAlertCandidateQueryTests
{
    [Fact]
    public async Task GetCandidatesAsync_returns_a_valid_certificate_expiring_within_the_window()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var memberId = Guid.NewGuid();
        var certificate = await SeedCertificateAsync(dbContext, memberId, new DateOnly(2026, 6, 15), "Valid");

        var candidates = await new ExpiryAlertCandidateQuery(dbContext)
            .GetCandidatesAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal(certificate.Id, candidate.SourceId);
        Assert.Equal(memberId, candidate.CrewMemberId);
        Assert.Equal(new DateOnly(2026, 6, 15), candidate.ExpiryDate);
        Assert.Equal("certificate", candidate.SourceType);
    }

    [Fact]
    public async Task GetCandidatesAsync_excludes_an_expired_certificate()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        await SeedCertificateAsync(dbContext, Guid.NewGuid(), new DateOnly(2026, 6, 15), "Expired");

        var candidates = await new ExpiryAlertCandidateQuery(dbContext)
            .GetCandidatesAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), CancellationToken.None);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task GetCandidatesAsync_excludes_a_revoked_certificate_even_when_its_expiry_falls_in_the_window()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var certificate = await SeedCertificateAsync(dbContext, Guid.NewGuid(), new DateOnly(2026, 6, 15), "Valid");
        certificate.Revoke(Guid.NewGuid(), "Issued in error", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();

        var candidates = await new ExpiryAlertCandidateQuery(dbContext)
            .GetCandidatesAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), CancellationToken.None);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task GetCandidatesAsync_excludes_a_certificate_expiring_outside_the_window()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        await SeedCertificateAsync(dbContext, Guid.NewGuid(), new DateOnly(2026, 7, 15), "Valid");

        var candidates = await new ExpiryAlertCandidateQuery(dbContext)
            .GetCandidatesAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), CancellationToken.None);

        Assert.Empty(candidates);
    }

    private static async Task<CrewCertificate> SeedCertificateAsync(
        HarborOpsDbContext dbContext, Guid crewMemberId, DateOnly expiryDate, string status)
    {
        var certificate = CrewCertificate.Create(
            crewMemberId, "STCW", "Basic Safety", "Authority", $"LIC-{Guid.NewGuid():N}"[..15],
            expiryDate.AddYears(-2), expiryDate, status);

        await dbContext.Set<CrewCertificate>().AddAsync(certificate);
        await dbContext.SaveChangesAsync();

        return certificate;
    }
}
