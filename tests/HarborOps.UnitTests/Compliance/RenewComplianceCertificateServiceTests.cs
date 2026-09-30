using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class RenewComplianceCertificateServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RenewAsync_creates_a_linked_successor_records_history_and_preserves_the_original()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedAsync(dbContext);
        var actorUserId = Guid.NewGuid();

        var response = await CreateService(dbContext).RenewAsync(
            original.Id, actorUserId, ValidRequest(original.ConcurrencyToken), CancellationToken.None);

        var successor = await dbContext.Set<ComplianceCertificate>().AsNoTracking().SingleAsync(item => item.Id == response.Id);
        Assert.Equal(original.Id, response.PredecessorCertificateId);
        Assert.Equal(original.Id, successor.PredecessorCertificateId);
        Assert.Equal(original.VesselId, successor.VesselId);
        Assert.Equal(original.CertificateType, successor.CertificateType);
        Assert.Equal("SMC-2027", successor.CertificateNumber);
        Assert.Equal("Flag State Administration", successor.IssuingAuthority);
        Assert.Equal(FixedNow.UtcDateTime, successor.CreatedUtc);

        var stored = await dbContext.Set<ComplianceCertificate>().AsNoTracking().SingleAsync(item => item.Id == original.Id);
        Assert.Equal("SMC-2022", stored.CertificateNumber);
        Assert.Equal(new DateOnly(2027, 1, 1), stored.ExpiresOn);
        Assert.False(stored.IsRevoked);

        var history = await dbContext.Set<ComplianceCertificateHistory>().SingleAsync();
        Assert.Equal(ComplianceCertificateHistory.RenewedEvent, history.EventType);
        Assert.Equal(original.Id, history.CertificateId);
        Assert.Equal(successor.Id, history.SuccessorCertificateId);
        Assert.Equal(actorUserId, history.ActorUserId);
        Assert.Equal(FixedNow.UtcDateTime, history.OccurredAtUtc);
    }

    [Fact]
    public async Task RenewAsync_rejects_a_second_renewal_of_the_same_certificate_and_attaches_later_renewals_to_the_latest()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedAsync(dbContext);
        var service = CreateService(dbContext);
        var first = await service.RenewAsync(original.Id, Guid.NewGuid(), ValidRequest(original.ConcurrencyToken), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ComplianceCertificateConflictException>(
            () => service.RenewAsync(original.Id, Guid.NewGuid(), ValidRequest(original.ConcurrencyToken) with { CertificateNumber = "SMC-DUP" }, CancellationToken.None));
        Assert.Contains("latest certificate", exception.Message, StringComparison.Ordinal);

        var latest = await dbContext.Set<ComplianceCertificate>().SingleAsync(item => item.Id == first.Id);
        var second = await service.RenewAsync(
            latest.Id,
            Guid.NewGuid(),
            ValidRequest(latest.ConcurrencyToken) with { CertificateNumber = "SMC-2032", IssuedOn = new DateOnly(2031, 12, 1), ExpiresOn = new DateOnly(2036, 12, 1) },
            CancellationToken.None);

        Assert.Equal(latest.Id, second.PredecessorCertificateId);
        Assert.Equal(1, await dbContext.Set<ComplianceCertificate>().CountAsync(item => item.PredecessorCertificateId == original.Id));
    }

    [Fact]
    public async Task RenewAsync_rejects_a_revoked_certificate()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedAsync(dbContext);
        original.Revoke(Guid.NewGuid(), "Issued in error by the flag state.", FixedNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ComplianceCertificateConflictException>(
            () => CreateService(dbContext).RenewAsync(original.Id, Guid.NewGuid(), ValidRequest(original.ConcurrencyToken), CancellationToken.None));
        Assert.Equal(1, await dbContext.Set<ComplianceCertificate>().CountAsync());
    }

    [Fact]
    public async Task RenewAsync_rejects_a_stale_concurrency_token()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ComplianceCertificateConflictException>(
            () => CreateService(dbContext).RenewAsync(original.Id, Guid.NewGuid(), ValidRequest(Guid.NewGuid()), CancellationToken.None));

        Assert.Contains("Reload", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await dbContext.Set<ComplianceCertificateHistory>().ToListAsync());
    }

    [Fact]
    public async Task RenewAsync_throws_for_an_unknown_certificate()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<ComplianceCertificateNotFoundException>(
            () => CreateService(dbContext).RenewAsync(Guid.NewGuid(), Guid.NewGuid(), ValidRequest(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task RenewAsync_rejects_missing_fields_and_token()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedAsync(dbContext);

        var exception = await Assert.ThrowsAsync<RenewComplianceCertificateValidationException>(
            () => CreateService(dbContext).RenewAsync(original.Id, Guid.NewGuid(), new RenewComplianceCertificateRequest(), CancellationToken.None));

        Assert.Contains(RenewComplianceCertificateValidator.ExpectedConcurrencyTokenField, exception.Errors);
        Assert.Contains(RenewComplianceCertificateValidator.CertificateNumberField, exception.Errors);
        Assert.Contains(RenewComplianceCertificateValidator.IssuingAuthorityField, exception.Errors);
        Assert.Contains(RenewComplianceCertificateValidator.IssuedOnField, exception.Errors);
        Assert.Contains(RenewComplianceCertificateValidator.ExpiresOnField, exception.Errors);
    }

    [Theory]
    [InlineData("2026-12-01", "2026-11-30", RenewComplianceCertificateValidator.ExpiresOnField)]
    [InlineData("2021-12-31", "2031-12-01", RenewComplianceCertificateValidator.IssuedOnField)]
    [InlineData("2026-12-01", "2027-01-01", RenewComplianceCertificateValidator.ExpiresOnField)]
    [InlineData("2026-12-01", "2026-12-31", RenewComplianceCertificateValidator.ExpiresOnField)]
    public async Task RenewAsync_rejects_invalid_dates(string issuedOn, string expiresOn, string field)
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedAsync(dbContext);
        var request = ValidRequest(original.ConcurrencyToken) with
        {
            IssuedOn = DateOnly.Parse(issuedOn),
            ExpiresOn = DateOnly.Parse(expiresOn),
        };

        var exception = await Assert.ThrowsAsync<RenewComplianceCertificateValidationException>(
            () => CreateService(dbContext).RenewAsync(original.Id, Guid.NewGuid(), request, CancellationToken.None));

        Assert.Contains(field, exception.Errors);
        Assert.Equal(1, await dbContext.Set<ComplianceCertificate>().CountAsync());
    }

    [Fact]
    public async Task RenewAsync_accepts_a_future_issued_successor_with_a_gap()
    {
        await using var dbContext = CreateDbContext();
        var original = await SeedAsync(dbContext);
        var request = ValidRequest(original.ConcurrencyToken) with
        {
            IssuedOn = new DateOnly(2027, 2, 1),
            ExpiresOn = new DateOnly(2032, 2, 1),
        };

        var response = await CreateService(dbContext).RenewAsync(original.Id, Guid.NewGuid(), request, CancellationToken.None);

        var successor = await dbContext.Set<ComplianceCertificate>().SingleAsync(item => item.Id == response.Id);
        Assert.Equal(new DateOnly(2027, 2, 1), successor.IssuedOn);
    }

    internal static RenewComplianceCertificateRequest ValidRequest(Guid token) => new()
    {
        ExpectedConcurrencyToken = token,
        CertificateNumber = " SMC-2027 ",
        IssuingAuthority = " Flag State Administration ",
        IssuedOn = new DateOnly(2026, 12, 1),
        ExpiresOn = new DateOnly(2031, 12, 1),
    };

    private static RenewComplianceCertificateService CreateService(HarborOpsDbContext dbContext) =>
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
