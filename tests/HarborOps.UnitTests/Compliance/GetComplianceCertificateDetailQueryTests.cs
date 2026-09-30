using System.Reflection;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class GetComplianceCertificateDetailQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_returns_the_whole_chain_oldest_first_from_any_certificate_in_it()
    {
        await using var dbContext = CreateDbContext();
        var original = Register(new DateOnly(2021, 1, 1), new DateOnly(2026, 6, 30));
        var second = original.Renew("SMC-2", "Flag State", new DateOnly(2026, 7, 1), new DateOnly(2027, 6, 30), FixedNow.UtcDateTime.AddDays(-2));
        var third = second.Renew("SMC-3", "Flag State", new DateOnly(2027, 8, 1), new DateOnly(2032, 7, 31), FixedNow.UtcDateTime.AddDays(-1));
        await dbContext.AddRangeAsync(third, original, second);
        await dbContext.SaveChangesAsync();

        foreach (var id in new[] { original.Id, second.Id, third.Id })
        {
            var response = await CreateQuery(dbContext).GetAsync(id, CancellationToken.None);

            Assert.NotNull(response);
            Assert.Equal(new[] { original.Id, second.Id, third.Id }, response.Chain.Select(item => item.Id));
        }

        var middle = (await CreateQuery(dbContext).GetAsync(second.Id, CancellationToken.None))!;
        Assert.Equal(original.Id, middle.PredecessorCertificateId);
        Assert.Equal(third.Id, middle.SuccessorCertificateId);
        Assert.False(middle.CanRenew);
        Assert.True(middle.CanRevoke);
        Assert.Equal(ComplianceCertificate.CurrentStatus, middle.Status);
        Assert.Null(middle.Chain[0].GapDaysAfterPredecessor);
        Assert.Equal(0, middle.Chain[1].GapDaysAfterPredecessor);
        Assert.True(middle.Chain[1].CoversPredecessorExpiry);
        Assert.Equal(31, middle.Chain[2].GapDaysAfterPredecessor);
        Assert.False(middle.Chain[2].CoversPredecessorExpiry);
        Assert.Equal(ComplianceCertificate.NotYetEffectiveStatus, middle.Chain[2].Status);
        Assert.Equal(ComplianceCertificate.ExpiredStatus, middle.Chain[0].Status);
    }

    [Fact]
    public async Task GetAsync_returns_history_for_the_whole_chain_newest_first()
    {
        await using var dbContext = CreateDbContext();
        var original = Register(new DateOnly(2022, 1, 1), new DateOnly(2027, 1, 1));
        var successor = original.Renew("SMC-2", "Flag State", new DateOnly(2027, 1, 2), new DateOnly(2032, 1, 1), FixedNow.UtcDateTime);
        successor.Revoke(Guid.NewGuid(), "Successor issued with a wrong IMO number.", FixedNow.UtcDateTime.AddHours(1));
        var renewed = ComplianceCertificateHistory.RecordRenewed(original.Id, successor.Id, Guid.NewGuid(), FixedNow.UtcDateTime);
        var revoked = ComplianceCertificateHistory.RecordRevoked(successor.Id, "Successor issued with a wrong IMO number.", Guid.NewGuid(), FixedNow.UtcDateTime.AddHours(1));
        await dbContext.AddRangeAsync(original, successor, renewed, revoked);
        await dbContext.SaveChangesAsync();

        var response = (await CreateQuery(dbContext).GetAsync(original.Id, CancellationToken.None))!;

        Assert.Equal(new[] { revoked.Id, renewed.Id }, response.History.Select(item => item.Id));
        Assert.Equal("Successor issued with a wrong IMO number.", response.History[0].Reason);
        Assert.Equal(successor.Id, response.History[1].SuccessorCertificateId);
        Assert.Equal(ComplianceCertificate.CurrentStatus, response.Status);
        Assert.False(response.CanRenew);
        Assert.Equal(ComplianceCertificate.RevokedStatus, response.Chain[1].Status);
        Assert.False(response.Chain[1].CoversPredecessorExpiry);
    }

    [Fact]
    public async Task GetAsync_reports_an_unrenewed_certificate_without_history_and_allows_both_actions()
    {
        await using var dbContext = CreateDbContext();
        var certificate = Register(new DateOnly(2022, 1, 1), new DateOnly(2027, 1, 1));
        await dbContext.AddAsync(certificate);
        await dbContext.SaveChangesAsync();

        var response = (await CreateQuery(dbContext).GetAsync(certificate.Id, CancellationToken.None))!;

        Assert.Single(response.Chain);
        Assert.Empty(response.History);
        Assert.Null(response.SuccessorCertificateId);
        Assert.True(response.CanRenew);
        Assert.True(response.CanRevoke);
        Assert.Equal(certificate.ConcurrencyToken, response.ConcurrencyToken);
    }

    [Fact]
    public async Task GetAsync_blocks_actions_on_a_revoked_certificate()
    {
        await using var dbContext = CreateDbContext();
        var certificate = Register(new DateOnly(2022, 1, 1), new DateOnly(2027, 1, 1));
        var actorUserId = Guid.NewGuid();
        certificate.Revoke(actorUserId, "Issued in error by the flag state.", FixedNow.UtcDateTime);
        await dbContext.AddAsync(certificate);
        await dbContext.SaveChangesAsync();

        var response = (await CreateQuery(dbContext).GetAsync(certificate.Id, CancellationToken.None))!;

        Assert.Equal(ComplianceCertificate.RevokedStatus, response.Status);
        Assert.Equal(actorUserId, response.RevokedByUserId);
        Assert.Equal("Issued in error by the flag state.", response.RevocationReason);
        Assert.False(response.CanRenew);
        Assert.False(response.CanRevoke);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_certificate()
    {
        await using var dbContext = CreateDbContext();

        Assert.Null(await CreateQuery(dbContext).GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public void BuildChain_terminates_on_a_cyclic_link_and_ignores_links_to_another_vessel_or_type()
    {
        var first = Register(new DateOnly(2022, 1, 1), new DateOnly(2027, 1, 1));
        var second = first.Renew("SMC-2", "Flag State", new DateOnly(2027, 1, 1), new DateOnly(2032, 1, 1), FixedNow.UtcDateTime);
        SetPredecessor(first, second.Id);
        var foreign = ComplianceCertificate.Register(Guid.NewGuid(), first.CertificateType, "F-1", "Flag State", new DateOnly(2027, 1, 1), new DateOnly(2032, 1, 1));
        SetPredecessor(foreign, second.Id);

        var chain = GetComplianceCertificateDetailQuery.BuildChain(first, [first, second, foreign]);

        Assert.Equal(new[] { second.Id, first.Id }, chain.Select(item => item.Id));
    }

    private static void SetPredecessor(ComplianceCertificate certificate, Guid predecessorId) =>
        typeof(ComplianceCertificate)
            .GetProperty(nameof(ComplianceCertificate.PredecessorCertificateId), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(certificate, predecessorId);

    private static ComplianceCertificate Register(DateOnly issuedOn, DateOnly expiresOn) =>
        ComplianceCertificate.Register(Guid.Parse("10000000-0000-0000-0000-000000000001"), "Safety Management Certificate", "SMC-1", "Flag State", issuedOn, expiresOn);

    private static GetComplianceCertificateDetailQuery CreateQuery(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedNow));

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
