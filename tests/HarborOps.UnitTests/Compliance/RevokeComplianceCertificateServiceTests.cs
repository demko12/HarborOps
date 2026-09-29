using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class RevokeComplianceCertificateServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RevokeAsync_records_actor_time_reason_and_history()
    {
        await using var dbContext = CreateDbContext();
        var certificate = await SeedAsync(dbContext);
        var actorUserId = Guid.NewGuid();

        var response = await CreateService(dbContext).RevokeAsync(
            certificate.Id,
            actorUserId,
            new RevokeComplianceCertificateRequest { ExpectedConcurrencyToken = certificate.ConcurrencyToken, Reason = "  Issued in error by the flag state.  " },
            CancellationToken.None);

        Assert.Equal(FixedNow.UtcDateTime, response.RevokedAtUtc);
        Assert.Equal(actorUserId, response.RevokedByUserId);

        var stored = await dbContext.Set<ComplianceCertificate>().AsNoTracking().SingleAsync();
        Assert.Equal(FixedNow.UtcDateTime, stored.RevokedAtUtc);
        Assert.Equal(actorUserId, stored.RevokedByUserId);
        Assert.Equal("Issued in error by the flag state.", stored.RevocationReason);

        var history = await dbContext.Set<ComplianceCertificateHistory>().SingleAsync();
        Assert.Equal(ComplianceCertificateHistory.RevokedEvent, history.EventType);
        Assert.Equal("Issued in error by the flag state.", history.Reason);
        Assert.Equal(actorUserId, history.ActorUserId);
        Assert.Null(history.SuccessorCertificateId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("Too short")]
    public async Task RevokeAsync_requires_a_substantive_reason(string? reason)
    {
        await using var dbContext = CreateDbContext();
        var certificate = await SeedAsync(dbContext);

        var exception = await Assert.ThrowsAsync<RevokeComplianceCertificateValidationException>(
            () => CreateService(dbContext).RevokeAsync(
                certificate.Id,
                Guid.NewGuid(),
                new RevokeComplianceCertificateRequest { ExpectedConcurrencyToken = certificate.ConcurrencyToken, Reason = reason },
                CancellationToken.None));

        Assert.Contains(RevokeComplianceCertificateValidator.ReasonField, exception.Errors);
    }

    [Fact]
    public async Task RevokeAsync_rejects_a_reason_longer_than_the_limit_and_a_missing_token()
    {
        await using var dbContext = CreateDbContext();
        var certificate = await SeedAsync(dbContext);

        var exception = await Assert.ThrowsAsync<RevokeComplianceCertificateValidationException>(
            () => CreateService(dbContext).RevokeAsync(
                certificate.Id,
                Guid.NewGuid(),
                new RevokeComplianceCertificateRequest { Reason = new string('x', ComplianceCertificate.RevocationReasonMaxLength + 1) },
                CancellationToken.None));

        Assert.Contains(RevokeComplianceCertificateValidator.ReasonField, exception.Errors);
        Assert.Contains(RevokeComplianceCertificateValidator.ExpectedConcurrencyTokenField, exception.Errors);
    }

    [Fact]
    public async Task RevokeAsync_rejects_a_stale_token_and_an_already_revoked_certificate()
    {
        await using var dbContext = CreateDbContext();
        var certificate = await SeedAsync(dbContext);
        var staleToken = certificate.ConcurrencyToken;
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<ComplianceCertificateConflictException>(
            () => service.RevokeAsync(certificate.Id, Guid.NewGuid(), Request(Guid.NewGuid()), CancellationToken.None));

        await service.RevokeAsync(certificate.Id, Guid.NewGuid(), Request(staleToken), CancellationToken.None);

        await Assert.ThrowsAsync<ComplianceCertificateConflictException>(
            () => service.RevokeAsync(certificate.Id, Guid.NewGuid(), Request(certificate.ConcurrencyToken), CancellationToken.None));
        Assert.Single(await dbContext.Set<ComplianceCertificateHistory>().ToListAsync());
    }

    [Fact]
    public async Task RevokeAsync_on_the_original_leaves_its_successor_valid()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedAsync(dbContext);
        var renewal = await new RenewComplianceCertificateService(dbContext, new FixedTimeProvider(FixedNow)).RenewAsync(
            original.Id, Guid.NewGuid(), RenewComplianceCertificateServiceTests.ValidRequest(original.ConcurrencyToken), CancellationToken.None);

        await CreateService(dbContext).RevokeAsync(original.Id, Guid.NewGuid(), Request(original.ConcurrencyToken), CancellationToken.None);

        var successor = await dbContext.Set<ComplianceCertificate>().AsNoTracking().SingleAsync(item => item.Id == renewal.Id);
        Assert.False(successor.IsRevoked);
        Assert.Equal(original.Id, successor.PredecessorCertificateId);
    }

    [Fact]
    public async Task RevokeAsync_throws_for_an_unknown_certificate()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<ComplianceCertificateNotFoundException>(
            () => CreateService(dbContext).RevokeAsync(Guid.NewGuid(), Guid.NewGuid(), Request(Guid.NewGuid()), CancellationToken.None));
    }

    private static RevokeComplianceCertificateRequest Request(Guid token) => new()
    {
        ExpectedConcurrencyToken = token,
        Reason = "Issued in error by the flag state.",
    };

    private static RevokeComplianceCertificateService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedNow));

    private static async Task<ComplianceCertificate> SeedAsync(HarborOpsDbContext dbContext)
    {
        var certificate = ComplianceCertificate.Register(
            Guid.NewGuid(), "Safety Management Certificate", "SMC-2022", "Flag State", new DateOnly(2022, 1, 1), new DateOnly(2027, 1, 1));
        await dbContext.AddAsync(certificate);
        await dbContext.SaveChangesAsync();

        return certificate;
    }

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
