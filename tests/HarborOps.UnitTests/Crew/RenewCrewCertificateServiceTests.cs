using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class RenewCrewCertificateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RenewAsync_rejects_invalid_input()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var certificate = await SeedCertificateAsync(dbContext);
        var request = new RenewCrewCertificateRequest
        {
            Name = string.Empty,
            IssuingAuthority = null,
            LicenseNumber = " ",
            IssueDate = null,
            ExpiryDate = null,
        };

        var exception = await Assert.ThrowsAsync<RenewCrewCertificateValidationException>(
            () => CreateService(dbContext).RenewAsync(certificate.Id, Guid.NewGuid(), request, CancellationToken.None));

        Assert.Equal(5, exception.Errors.Count);
    }

    [Fact]
    public async Task RenewAsync_throws_when_the_certificate_does_not_exist()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var unknownId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<CrewCertificateNotFoundException>(
            () => CreateService(dbContext).RenewAsync(unknownId, Guid.NewGuid(), ValidRequest("LIC-NEW"), CancellationToken.None));

        Assert.Equal(unknownId, exception.CertificateId);
    }

    [Fact]
    public async Task RenewAsync_creates_a_linked_successor_and_preserves_the_original()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var certificate = await SeedCertificateAsync(dbContext, licenseNumber: "LIC-ORIG", expiryDate: new DateOnly(2026, 3, 1));
        var actorUserId = Guid.NewGuid();

        var response = await CreateService(dbContext).RenewAsync(
            certificate.Id, actorUserId, ValidRequest("LIC-NEW"), CancellationToken.None);

        Assert.Equal(certificate.Id, response.PredecessorCertificateId);
        Assert.Equal(certificate.CrewMemberId, response.CrewMemberId);
        Assert.Equal(certificate.Type, response.Type);
        Assert.Equal("Valid", response.Status);

        var original = await dbContext.Set<CrewCertificate>().SingleAsync(c => c.Id == certificate.Id);
        Assert.Equal("LIC-ORIG", original.LicenseNumber);
        Assert.Equal(new DateOnly(2026, 3, 1), original.ExpiryDate);
        Assert.Null(original.PredecessorCertificateId);

        var successor = await dbContext.Set<CrewCertificate>().SingleAsync(c => c.Id == response.Id);
        Assert.Equal("LIC-NEW", successor.LicenseNumber);
        Assert.Equal(certificate.Id, successor.PredecessorCertificateId);

        var history = await dbContext.Set<CrewCertificateHistory>().SingleAsync(h => h.CertificateId == successor.Id);
        Assert.Equal(CrewCertificateHistory.RenewedEvent, history.EventType);
        Assert.Equal(actorUserId, history.ActorUserId);
        Assert.Equal(certificate.Id, history.PredecessorCertificateId);
    }

    [Fact]
    public async Task RenewAsync_computes_expired_status_when_the_new_expiry_date_is_in_the_past()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var certificate = await SeedCertificateAsync(dbContext);

        var request = ValidRequest("LIC-NEW") with { ExpiryDate = new DateOnly(2026, 1, 1), IssueDate = new DateOnly(2025, 1, 1) };
        var response = await CreateService(dbContext).RenewAsync(certificate.Id, Guid.NewGuid(), request, CancellationToken.None);

        Assert.Equal("Expired", response.Status);
    }

    [Fact]
    public async Task RenewAsync_allows_carrying_forward_the_same_license_number_as_the_predecessor()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var certificate = await SeedCertificateAsync(dbContext, licenseNumber: "LIC-SAME");

        var response = await CreateService(dbContext).RenewAsync(
            certificate.Id, Guid.NewGuid(), ValidRequest("LIC-SAME"), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
    }

    [Fact]
    public async Task RenewAsync_rejects_a_duplicate_license_number_against_an_unrelated_certificate()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var certificate = await SeedCertificateAsync(dbContext);
        var unrelated = CrewCertificate.Create(
            certificate.CrewMemberId, "Medical", "Fitness Certificate", "Authority", "LIC-MEDICAL",
            new DateOnly(2026, 1, 1), new DateOnly(2028, 1, 1), "Valid");
        await dbContext.Set<CrewCertificate>().AddAsync(unrelated);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<DuplicateCrewCertificateLicenseException>(
            () => CreateService(dbContext).RenewAsync(
                certificate.Id, Guid.NewGuid(), ValidRequest(unrelated.LicenseNumber), CancellationToken.None));

        Assert.Equal(certificate.CrewMemberId, exception.CrewMemberId);
    }

    [Fact]
    public async Task RenewAsync_rejects_a_second_renewal_of_the_same_predecessor()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var certificate = await SeedCertificateAsync(dbContext);
        await CreateService(dbContext).RenewAsync(certificate.Id, Guid.NewGuid(), ValidRequest("LIC-FIRST"), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateCrewCertificateRenewalException>(
            () => CreateService(dbContext).RenewAsync(certificate.Id, Guid.NewGuid(), ValidRequest("LIC-SECOND"), CancellationToken.None));

        Assert.Equal(certificate.Id, exception.PredecessorCertificateId);
    }

    [Fact]
    public async Task RenewAsync_supports_successive_renewals_through_the_latest_certificate_in_the_chain()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var original = await SeedCertificateAsync(dbContext, licenseNumber: "LIC-A", expiryDate: new DateOnly(2025, 1, 1));

        var firstRenewal = await CreateService(dbContext).RenewAsync(
            original.Id,
            Guid.NewGuid(),
            ValidRequest("LIC-B") with { IssueDate = new DateOnly(2025, 1, 2), ExpiryDate = new DateOnly(2026, 1, 1) },
            CancellationToken.None);
        var secondRenewal = await CreateService(dbContext).RenewAsync(
            firstRenewal.Id,
            Guid.NewGuid(),
            ValidRequest("LIC-C") with { IssueDate = new DateOnly(2026, 1, 2), ExpiryDate = new DateOnly(2027, 1, 1) },
            CancellationToken.None);

        Assert.Equal(firstRenewal.Id, secondRenewal.PredecessorCertificateId);
        var chainCount = await dbContext.Set<CrewCertificate>().CountAsync(c => c.CrewMemberId == original.CrewMemberId);
        Assert.Equal(3, chainCount);
    }

    private static async Task<CrewCertificate> SeedCertificateAsync(
        HarborOpsDbContext dbContext, string licenseNumber = "LIC-ORIG", DateOnly? expiryDate = null)
    {
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        var certificate = CrewCertificate.Create(
            member.Id, "STCW", "Basic Safety", "Authority", licenseNumber,
            new DateOnly(2024, 1, 1), expiryDate ?? new DateOnly(2026, 3, 1), "Valid");

        await dbContext.Set<CrewCertificate>().AddAsync(certificate);
        await dbContext.SaveChangesAsync();

        return certificate;
    }

    private static RenewCrewCertificateRequest ValidRequest(string licenseNumber) => new()
    {
        Name = "Basic Safety Training Certificate",
        IssuingAuthority = "Flag State Maritime Authority",
        LicenseNumber = licenseNumber,
        IssueDate = new DateOnly(2026, 3, 1),
        ExpiryDate = new DateOnly(2028, 3, 1),
    };

    private static RenewCrewCertificateService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));
}
