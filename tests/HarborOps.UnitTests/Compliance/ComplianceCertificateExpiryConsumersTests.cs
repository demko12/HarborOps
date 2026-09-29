using HarborOps.Api.Composition.Notifications;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.Modules.Notifications.Application;
using HarborOps.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CrewContracts = HarborOps.Modules.Crew.Contracts;
using DocumentContracts = HarborOps.Modules.Documents.Contracts;

namespace HarborOps.UnitTests.Compliance;

public sealed class ComplianceCertificateExpiryConsumersTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);
    private static readonly DateOnly Expiry = Today.AddDays(30);
    private static readonly DateTime NowUtc = new(2026, 9, 28, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Expiry_candidates_keep_an_original_without_a_successor()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedOriginalAsync(dbContext);

        var candidates = await CandidatesAsync(dbContext);

        Assert.Equal(original.Id, Assert.Single(candidates).SourceId);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Expiry_candidates_suppress_an_original_covered_by_a_successor_even_when_it_is_not_yet_effective(int issuedAfterExpiryDays)
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedOriginalAsync(dbContext);
        await RenewAsync(dbContext, original, Expiry.AddDays(issuedAfterExpiryDays));

        Assert.Empty(await CandidatesAsync(dbContext));
    }

    [Fact]
    public async Task Expiry_candidates_keep_an_original_when_the_successor_leaves_a_gap()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedOriginalAsync(dbContext);
        await RenewAsync(dbContext, original, Expiry.AddDays(2));

        Assert.Equal(original.Id, Assert.Single(await CandidatesAsync(dbContext)).SourceId);
    }

    [Fact]
    public async Task Expiry_candidates_keep_an_original_whose_successor_is_revoked()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedOriginalAsync(dbContext);
        var successor = await RenewAsync(dbContext, original, Expiry);
        successor.Revoke(Guid.NewGuid(), "Successor issued with a wrong IMO number.", NowUtc);
        await dbContext.SaveChangesAsync();

        Assert.Equal(original.Id, Assert.Single(await CandidatesAsync(dbContext)).SourceId);
    }

    [Fact]
    public async Task Expiry_candidates_exclude_a_revoked_certificate_immediately()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedOriginalAsync(dbContext);
        original.Revoke(Guid.NewGuid(), "Issued in error by the flag state.", NowUtc);
        await dbContext.SaveChangesAsync();

        Assert.Empty(await CandidatesAsync(dbContext));
    }

    [Fact]
    public async Task Dashboard_excludes_covered_certificates_and_keeps_uncovered_and_revoked_risk()
    {
        await using var dbContext = CreateDbContext();
        var covered = await SeedOriginalAsync(dbContext, "Covered");
        await RenewAsync(dbContext, covered, Expiry.AddDays(1));
        var gapped = await SeedOriginalAsync(dbContext, "Gapped");
        await RenewAsync(dbContext, gapped, Expiry.AddDays(5));
        var revoked = await SeedOriginalAsync(dbContext, "Revoked");
        revoked.Revoke(Guid.NewGuid(), "Issued in error by the flag state.", NowUtc);
        var untouched = await SeedOriginalAsync(dbContext, "Untouched");
        await dbContext.SaveChangesAsync();

        var result = await DashboardAsync(dbContext);

        Assert.Equal(3, result.AttentionItemCount);
        Assert.Equal(
            new[] { gapped.Id.ToString(), untouched.Id.ToString(), revoked.Id.ToString() }.Order(StringComparer.Ordinal),
            result.Items.Select(item => item.SortKey).Order(StringComparer.Ordinal));
        var revokedItem = Assert.Single(result.Items, item => item.SortKey == revoked.Id.ToString());
        Assert.Equal(OperationsDashboardComplianceQuery.RevokedCertificateItemType, revokedItem.ItemType);
        Assert.Equal(ComplianceCertificate.RevokedStatus, revokedItem.Severity);
        Assert.Equal(Today, revokedItem.DueDate);
        Assert.Equal("Certificate", Assert.Single(result.Items, item => item.SortKey == gapped.Id.ToString()).ItemType);
    }

    [Fact]
    public async Task Dashboard_hides_a_revoked_certificate_when_a_current_certificate_of_the_same_type_replaces_it()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var revoked = ComplianceCertificate.Register(vesselId, "Safety Management Certificate", "SMC-REVOKED", "Flag State", Today.AddYears(-1), Today.AddYears(2));
        revoked.Revoke(Guid.NewGuid(), "Issued in error by the flag state.", NowUtc);
        var replacement = ComplianceCertificate.Register(vesselId, "Safety Management Certificate", "SMC-NEW", "Flag State", Today, Today.AddYears(5));
        await dbContext.AddRangeAsync(revoked, replacement);
        await dbContext.SaveChangesAsync();

        var result = await DashboardAsync(dbContext);

        Assert.Equal(0, result.AttentionItemCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Dashboard_keeps_a_revoked_certificate_when_the_only_replacement_is_not_yet_effective_other_type_or_other_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var revoked = ComplianceCertificate.Register(vesselId, "Safety Management Certificate", "SMC-REVOKED", "Flag State", Today.AddYears(-1), Today.AddYears(2));
        revoked.Revoke(Guid.NewGuid(), "Issued in error by the flag state.", NowUtc);
        await dbContext.AddRangeAsync(
            revoked,
            ComplianceCertificate.Register(vesselId, "Safety Management Certificate", "SMC-FUTURE", "Flag State", Today.AddDays(1), Today.AddYears(5)),
            ComplianceCertificate.Register(vesselId, "Load Line Certificate", "LL-1", "Flag State", Today.AddYears(-1), Today.AddYears(5)),
            ComplianceCertificate.Register(Guid.NewGuid(), "Safety Management Certificate", "SMC-OTHER", "Flag State", Today.AddYears(-1), Today.AddYears(5)));
        await dbContext.SaveChangesAsync();

        var result = await DashboardAsync(dbContext);

        Assert.Equal(1, result.AttentionItemCount);
        Assert.Equal(revoked.Id.ToString(), Assert.Single(result.Items).SortKey);
    }

    [Fact]
    public async Task Dashboard_does_not_keep_a_revoked_certificate_after_its_own_expiry()
    {
        await using var dbContext = CreateDbContext();
        var revoked = ComplianceCertificate.Register(Guid.NewGuid(), "Safety Management Certificate", "SMC-OLD", "Flag State", Today.AddYears(-5), Today.AddDays(-1));
        revoked.Revoke(Guid.NewGuid(), "Issued in error by the flag state.", NowUtc);
        await dbContext.AddAsync(revoked);
        await dbContext.SaveChangesAsync();

        var result = await DashboardAsync(dbContext);

        Assert.Equal(0, result.AttentionItemCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Dashboard_keeps_a_revoked_certificate_on_its_inclusive_expiry_day()
    {
        await using var dbContext = CreateDbContext();
        var revoked = ComplianceCertificate.Register(Guid.NewGuid(), "Safety Management Certificate", "SMC-LAST-DAY", "Flag State", Today.AddYears(-5), Today);
        revoked.Revoke(Guid.NewGuid(), "Issued in error by the flag state.", NowUtc);
        await dbContext.AddAsync(revoked);
        await dbContext.SaveChangesAsync();

        var result = await DashboardAsync(dbContext);

        Assert.Equal(revoked.Id.ToString(), Assert.Single(result.Items).SortKey);
    }

    [Fact]
    public async Task Dashboard_shows_the_still_valid_original_instead_of_its_revoked_future_successor()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedOriginalAsync(dbContext);
        var successor = await RenewAsync(dbContext, original, Expiry);
        successor.Revoke(Guid.NewGuid(), "Successor issued with a wrong IMO number.", NowUtc);
        await dbContext.SaveChangesAsync();

        var result = await DashboardAsync(dbContext);

        var item = Assert.Single(result.Items);
        Assert.Equal(original.Id.ToString(), item.SortKey);
        Assert.Equal("Certificate", item.ItemType);
    }

    [Fact]
    public async Task Generator_stops_new_alerts_after_revocation_and_keeps_delivered_notifications()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedOriginalAsync(dbContext);
        var generator = CreateGenerator(dbContext);

        await generator.GenerateAsync(CancellationToken.None);
        var delivered = Assert.Single(await dbContext.Set<OperationAlert>().AsNoTracking().ToListAsync());
        Assert.Equal(original.Id, delivered.SourceId);

        original.Revoke(Guid.NewGuid(), "Issued in error by the flag state.", NowUtc);
        await dbContext.SaveChangesAsync();
        await generator.GenerateAsync(CancellationToken.None);

        var alerts = await dbContext.Set<OperationAlert>().AsNoTracking().ToListAsync();
        Assert.Equal(delivered.Id, Assert.Single(alerts).Id);
    }

    [Fact]
    public async Task Generator_skips_a_covered_original_and_alerts_on_an_uncovered_one()
    {
        await using var dbContext = CreateDbContext();
        var covered = await SeedOriginalAsync(dbContext, "Covered");
        await RenewAsync(dbContext, covered, Expiry.AddDays(1));
        var gapped = await SeedOriginalAsync(dbContext, "Gapped");
        await RenewAsync(dbContext, gapped, Expiry.AddDays(2));

        await CreateGenerator(dbContext).GenerateAsync(CancellationToken.None);

        var alert = Assert.Single(await dbContext.Set<OperationAlert>().AsNoTracking().ToListAsync());
        Assert.Equal(gapped.Id, alert.SourceId);
        Assert.Equal("compliance", alert.Capability);
    }

    private static Task<IReadOnlyList<HarborOps.Modules.Compliance.Contracts.ComplianceExpiryAlertCandidate>> CandidatesAsync(HarborOpsDbContext dbContext) =>
        new ExpiryAlertCandidateQuery(dbContext).GetCandidatesAsync(Today, Today.AddDays(90), CancellationToken.None);

    private static Task<HarborOps.Modules.Compliance.Contracts.OperationsDashboardComplianceResult> DashboardAsync(HarborOpsDbContext dbContext) =>
        new OperationsDashboardComplianceQuery(dbContext, new FixedTimeProvider(new DateTimeOffset(NowUtc)))
            .GetAttentionItemsAsync(Today.AddDays(30), 10, CancellationToken.None);

    private static ExpiryAlertGenerator CreateGenerator(HarborOpsDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(NowUtc));

        return new ExpiryAlertGenerator(
            new EmptyDocumentCandidates(),
            new EmptyCrewCandidates(),
            new ExpiryAlertCandidateQuery(dbContext),
            new NotificationService(dbContext, timeProvider),
            timeProvider,
            Options.Create(new ExpiryAlertOptions { ThresholdDays = [90, 30, 7] }));
    }

    private static async Task<ComplianceCertificate> SeedOriginalAsync(HarborOpsDbContext dbContext, string number = "SMC-1")
    {
        var certificate = ComplianceCertificate.Register(Guid.NewGuid(), "Safety Management Certificate", number, "Flag State", Today.AddYears(-5), Expiry);
        await dbContext.AddAsync(certificate);
        await dbContext.SaveChangesAsync();

        return certificate;
    }

    private static async Task<ComplianceCertificate> RenewAsync(HarborOpsDbContext dbContext, ComplianceCertificate original, DateOnly issuedOn)
    {
        var successor = original.Renew("SMC-NEXT", "Flag State", issuedOn, Expiry.AddYears(5), NowUtc);
        await dbContext.AddAsync(successor);
        await dbContext.SaveChangesAsync();

        return successor;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class EmptyDocumentCandidates : DocumentContracts.IExpiryAlertCandidateQuery
    {
        public Task<IReadOnlyList<DocumentContracts.ExpiryAlertCandidate>> GetCandidatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentContracts.ExpiryAlertCandidate>>([]);
    }

    private sealed class EmptyCrewCandidates : CrewContracts.IExpiryAlertCandidateQuery
    {
        public Task<IReadOnlyList<CrewContracts.CrewExpiryAlertCandidate>> GetCandidatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CrewContracts.CrewExpiryAlertCandidate>>([]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
