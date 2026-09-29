using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

public sealed class ComplianceCertificateRenewalRelationalTests : IAsyncLifetime
{
    private const int Rounds = 5;
    private const int SqlServerDeadlockErrorNumber = 1205;

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    [RelationalFact]
    public async Task Competing_renewals_of_one_certificate_link_exactly_one_successor()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var original = await SeedAsync();

            var (first, second) = await RaceAsync(
                () => RenewAsync(original.Id, original.ConcurrencyToken, "SMC-A"),
                () => RenewAsync(original.Id, original.ConcurrencyToken, "SMC-B"));

            AssertExpectedConflicts(first, second);
            Assert.True(first is null ^ second is null, $"Round {round}: expected exactly one renewal to succeed.");
            Assert.Equal(1, await SuccessorCountAsync(original.Id));
            Assert.Equal(1, await HistoryCountAsync(original.Id));
        }
    }

    [RelationalFact]
    public async Task Racing_renewal_and_revocation_with_the_same_token_never_both_apply()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var original = await SeedAsync();

            var (renewal, revocation) = await RaceAsync(
                () => RenewAsync(original.Id, original.ConcurrencyToken, "SMC-A"),
                () => RevokeAsync(original.Id, original.ConcurrencyToken));

            AssertExpectedConflicts(renewal, revocation);
            Assert.True(renewal is null ^ revocation is null, $"Round {round}: expected exactly one change to apply.");
            Assert.Equal(renewal is null ? 1 : 0, await SuccessorCountAsync(original.Id));
            Assert.Equal(revocation is null, await IsRevokedAsync(original.Id));
        }
    }

    [RelationalFact]
    public async Task Database_rejects_a_second_direct_successor_even_without_the_service()
    {
        var original = await SeedAsync();
        await RenewAsync(original.Id, original.ConcurrencyToken, "SMC-A");

        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<SqlException>(() => dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO [compliance].[ComplianceCertificates]
                ([Id], [VesselId], [CertificateType], [CertificateNumber], [IssuingAuthority],
                 [IssuedOn], [ExpiresOn], [CreatedUtc], [PredecessorCertificateId])
            VALUES
                ({Guid.NewGuid()}, {original.VesselId}, {original.CertificateType}, N'SMC-B', N'Flag State',
                 {new DateOnly(2026, 12, 1)}, {new DateOnly(2031, 12, 1)}, {DateTime.UtcNow}, {original.Id});
            """));
        Assert.Equal(1, await SuccessorCountAsync(original.Id));
    }

    private static async Task<(Exception? First, Exception? Second)> RaceAsync(
        Func<Task> first,
        Func<Task> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> RunAsync(Func<Task> operation)
        {
            await start.Task;

            try
            {
                await operation();

                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var firstTask = Task.Run(() => RunAsync(first));
        var secondTask = Task.Run(() => RunAsync(second));
        start.SetResult();

        return (await firstTask, await secondTask);
    }

    private static void AssertExpectedConflicts(params Exception?[] outcomes)
    {
        foreach (var outcome in outcomes.Where(outcome => outcome is not null))
        {
            var expected = outcome
                is ComplianceCertificateConflictException
                or DbUpdateException
                || IsDeadlockVictim(outcome!);

            Assert.True(expected, $"Unexpected failure: {outcome}");
        }
    }

    private static bool IsDeadlockVictim(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: SqlServerDeadlockErrorNumber })
            {
                return true;
            }
        }

        return false;
    }

    private async Task RenewAsync(Guid certificateId, Guid token, string number)
    {
        await using var dbContext = CreateDbContext();

        await new RenewComplianceCertificateService(dbContext, TimeProvider.System).RenewAsync(
            certificateId,
            Guid.NewGuid(),
            new RenewComplianceCertificateRequest
            {
                ExpectedConcurrencyToken = token,
                CertificateNumber = number,
                IssuingAuthority = "Flag State",
                IssuedOn = new DateOnly(2026, 12, 1),
                ExpiresOn = new DateOnly(2031, 12, 1),
            },
            CancellationToken.None);
    }

    private async Task RevokeAsync(Guid certificateId, Guid token)
    {
        await using var dbContext = CreateDbContext();

        await new RevokeComplianceCertificateService(dbContext, TimeProvider.System).RevokeAsync(
            certificateId,
            Guid.NewGuid(),
            new RevokeComplianceCertificateRequest
            {
                ExpectedConcurrencyToken = token,
                Reason = "Issued in error by the flag state.",
            },
            CancellationToken.None);
    }

    private async Task<ComplianceCertificate> SeedAsync()
    {
        await using var dbContext = CreateDbContext();
        var certificate = ComplianceCertificate.Register(
            Guid.NewGuid(), "Safety Management Certificate", "SMC-2022", "Flag State", new DateOnly(2022, 1, 1), new DateOnly(2027, 1, 1));
        await dbContext.AddAsync(certificate);
        await dbContext.SaveChangesAsync();

        return certificate;
    }

    private async Task<int> SuccessorCountAsync(Guid certificateId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<ComplianceCertificate>()
            .CountAsync(item => item.PredecessorCertificateId == certificateId);
    }

    private async Task<int> HistoryCountAsync(Guid certificateId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<ComplianceCertificateHistory>()
            .CountAsync(item => item.CertificateId == certificateId);
    }

    private async Task<bool> IsRevokedAsync(Guid certificateId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<ComplianceCertificate>()
            .Where(item => item.Id == certificateId)
            .Select(item => item.RevokedAtUtc != null)
            .SingleAsync();
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }
}
