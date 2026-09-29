using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HarborOps.IntegrationTests.Relational;

public sealed class ComplianceCertificateRenewalMigrationRelationalTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260924125612_AddMaintenanceSupplierReturns";

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
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
    public async Task Upgrading_keeps_existing_certificates_valid_without_fabricated_history()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var vesselId = Guid.NewGuid();

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO [compliance].[ComplianceCertificates]
                    ([Id], [VesselId], [CertificateType], [CertificateNumber], [IssuingAuthority], [IssuedOn], [ExpiresOn], [CreatedUtc])
                VALUES
                    ({firstId}, {vesselId}, N'Safety Management Certificate', N'SMC-2022', N'Flag State',
                     {new DateOnly(2022, 1, 1)}, {new DateOnly(2027, 1, 1)}, {new DateTime(2022, 1, 2, 8, 0, 0, DateTimeKind.Utc)}),
                    ({secondId}, {vesselId}, N'Load Line Certificate', N'LL-2021', N'Class Society',
                     {new DateOnly(2021, 6, 1)}, {new DateOnly(2026, 6, 1)}, {new DateTime(2021, 6, 2, 8, 0, 0, DateTimeKind.Utc)});
                """);

            await dbContext.Database.MigrateAsync();
        }

        await using var readContext = CreateDbContext();
        var certificates = await readContext.Set<ComplianceCertificate>()
            .AsNoTracking()
            .Where(certificate => certificate.VesselId == vesselId)
            .OrderBy(certificate => certificate.CertificateNumber)
            .ToListAsync();

        Assert.Equal(new[] { "LL-2021", "SMC-2022" }, certificates.Select(certificate => certificate.CertificateNumber));
        Assert.All(certificates, certificate =>
        {
            Assert.Null(certificate.PredecessorCertificateId);
            Assert.Null(certificate.RevokedAtUtc);
            Assert.Null(certificate.RevokedByUserId);
            Assert.Null(certificate.RevocationReason);
            Assert.NotEqual(Guid.Empty, certificate.ConcurrencyToken);
        });
        Assert.NotEqual(certificates[0].ConcurrencyToken, certificates[1].ConcurrencyToken);
        Assert.Equal(new DateOnly(2027, 1, 1), certificates.Single(certificate => certificate.Id == firstId).ExpiresOn);
        Assert.Empty(await readContext.Set<ComplianceCertificateHistory>().ToListAsync());
        Assert.Empty(await readContext.Database.GetPendingMigrationsAsync());
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }
}
