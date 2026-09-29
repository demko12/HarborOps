using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class GetCrewCertificateHistoryQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_throws_when_the_certificate_does_not_exist()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var unknownId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<CrewCertificateNotFoundException>(
            () => new GetCrewCertificateHistoryQuery(dbContext).GetAsync(unknownId, CancellationToken.None));

        Assert.Equal(unknownId, exception.CertificateId);
    }

    [Fact]
    public async Task GetAsync_returns_only_the_certificate_itself_when_it_has_never_been_renewed()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var certificate = await SeedCertificateAsync(dbContext);

        var response = await new GetCrewCertificateHistoryQuery(dbContext).GetAsync(certificate.Id, CancellationToken.None);

        Assert.Equal([certificate.Id], response.ChainCertificateIds);
        Assert.Empty(response.History);
    }

    [Fact]
    public async Task GetAsync_orders_the_chain_oldest_to_newest_across_successive_renewals()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var original = await SeedCertificateAsync(dbContext, licenseNumber: "LIC-A", expiryDate: new DateOnly(2025, 1, 1));
        var renewService = new RenewCrewCertificateService(dbContext, new FixedTimeProvider(Now));

        var firstRenewal = await renewService.RenewAsync(
            original.Id, Guid.NewGuid(), RenewalRequest("LIC-B", new DateOnly(2025, 1, 2), new DateOnly(2026, 1, 1)), CancellationToken.None);
        var secondRenewal = await renewService.RenewAsync(
            firstRenewal.Id, Guid.NewGuid(), RenewalRequest("LIC-C", new DateOnly(2026, 1, 2), new DateOnly(2027, 1, 1)), CancellationToken.None);

        var response = await new GetCrewCertificateHistoryQuery(dbContext).GetAsync(original.Id, CancellationToken.None);

        Assert.Equal([original.Id, firstRenewal.Id, secondRenewal.Id], response.ChainCertificateIds);
    }

    [Fact]
    public async Task GetAsync_returns_the_same_chain_regardless_of_which_certificate_in_it_is_requested()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var original = await SeedCertificateAsync(dbContext, licenseNumber: "LIC-A", expiryDate: new DateOnly(2025, 1, 1));
        var renewService = new RenewCrewCertificateService(dbContext, new FixedTimeProvider(Now));
        var renewal = await renewService.RenewAsync(
            original.Id, Guid.NewGuid(), RenewalRequest("LIC-B", new DateOnly(2025, 1, 2), new DateOnly(2026, 1, 1)), CancellationToken.None);

        var fromOriginal = await new GetCrewCertificateHistoryQuery(dbContext).GetAsync(original.Id, CancellationToken.None);
        var fromRenewal = await new GetCrewCertificateHistoryQuery(dbContext).GetAsync(renewal.Id, CancellationToken.None);

        Assert.Equal(fromOriginal.ChainCertificateIds, fromRenewal.ChainCertificateIds);
    }

    [Fact]
    public async Task GetAsync_includes_renewed_and_revoked_history_entries_for_every_certificate_in_the_chain()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var original = await SeedCertificateAsync(dbContext, licenseNumber: "LIC-A", expiryDate: new DateOnly(2025, 1, 1));
        var renewal = await new RenewCrewCertificateService(dbContext, new FixedTimeProvider(Now)).RenewAsync(
            original.Id, Guid.NewGuid(), RenewalRequest("LIC-B", new DateOnly(2025, 1, 2), new DateOnly(2026, 1, 1)), CancellationToken.None);
        await new RevokeCrewCertificateService(dbContext, new FixedTimeProvider(Now)).RevokeAsync(
            original.Id, Guid.NewGuid(), new RevokeCrewCertificateRequest { Reason = "Superseded" }, CancellationToken.None);

        var response = await new GetCrewCertificateHistoryQuery(dbContext).GetAsync(renewal.Id, CancellationToken.None);

        Assert.Equal(2, response.History.Count);
        Assert.Contains(response.History, entry => entry.EventType == CrewCertificateHistory.RenewedEvent && entry.CertificateId == renewal.Id);
        Assert.Contains(response.History, entry => entry.EventType == CrewCertificateHistory.RevokedEvent && entry.CertificateId == original.Id);
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

    private static RenewCrewCertificateRequest RenewalRequest(string licenseNumber, DateOnly issueDate, DateOnly expiryDate) => new()
    {
        Name = "Basic Safety Training Certificate",
        IssuingAuthority = "Flag State Maritime Authority",
        LicenseNumber = licenseNumber,
        IssueDate = issueDate,
        ExpiryDate = expiryDate,
    };
}
