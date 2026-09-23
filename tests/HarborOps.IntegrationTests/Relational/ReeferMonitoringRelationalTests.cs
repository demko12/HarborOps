using System.Globalization;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

public sealed class ReeferMonitoringRelationalTests : IAsyncLifetime
{
    private static readonly Guid ManagerUserId =
        Guid.Parse("4d5e6f70-8192-4a3b-8c4d-5e6f708192a3");

    private static readonly Guid OperatorUserId =
        Guid.Parse("1a2b3c4d-5e6f-4071-8293-a4b5c6d7e8f9");

    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 19, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTime MeasuredAtUtc =
        new(2026, 9, 19, 7, 45, 0, DateTimeKind.Utc);

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
    public async Task The_migration_creates_every_reefer_table_in_the_cargo_schema()
    {
        await using var dbContext = CreateDbContext();

        var tables = await QueryStringsAsync(
            dbContext,
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'cargo'");

        Assert.Contains("ReeferMonitoringProfiles", tables);
        Assert.Contains("ReeferProfileChanges", tables);
        Assert.Contains("ReeferTemperatureReadings", tables);
        Assert.Contains("ReeferExcursions", tables);
    }

    [RelationalFact]
    public async Task Every_temperature_column_is_mapped_with_two_decimal_places()
    {
        await using var dbContext = CreateDbContext();

        var columns = await QueryStringsAsync(
            dbContext,
            """
            SELECT CONCAT(TABLE_NAME, '.', COLUMN_NAME, ':', NUMERIC_PRECISION, ',', NUMERIC_SCALE)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'cargo'
              AND TABLE_NAME LIKE 'Reefer%'
              AND DATA_TYPE = 'decimal'
            """);

        Assert.NotEmpty(columns);
        Assert.All(columns, column => Assert.EndsWith(":5,2", column, StringComparison.Ordinal));
    }

    [RelationalFact]
    public async Task The_open_excursion_index_is_filtered_to_open_rows()
    {
        await using var dbContext = CreateDbContext();

        var filters = await QueryStringsAsync(
            dbContext,
            """
            SELECT filter_definition
            FROM sys.indexes
            WHERE name = 'IX_ReeferExcursions_OpenContainerId'
            """);

        Assert.Equal("([Status]='Open')", Assert.Single(filters));
    }

    [RelationalFact]
    public async Task A_temperature_at_the_supported_boundary_round_trips_unchanged()
    {
        var containerId = await SeedProfiledReeferAsync();

        await using (var writeContext = CreateDbContext())
        {
            await RecordAsync(writeContext, containerId, -59.99m);
        }

        await using var readContext = CreateDbContext();
        var reading = await readContext.Set<ReeferTemperatureReading>()
            .SingleAsync(candidate => candidate.ContainerId == containerId);

        Assert.Equal(-59.99m, reading.TemperatureCelsius);
        Assert.Equal(-2m, reading.ThresholdMinimumCelsius);
    }

    [RelationalFact]
    public async Task A_reading_can_only_carry_one_excursion()
    {
        var containerId = await SeedProfiledReeferAsync();
        Guid readingId;

        await using (var writeContext = CreateDbContext())
        {
            var recorded = await RecordAsync(writeContext, containerId, 6.5m);
            readingId = recorded.Reading.Id;
        }

        await using var duplicateContext = CreateDbContext();
        var reading = await duplicateContext.Set<ReeferTemperatureReading>()
            .SingleAsync(candidate => candidate.Id == readingId);

        await duplicateContext.Set<ReeferExcursion>()
            .AddAsync(ReeferExcursion.Open(reading));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => duplicateContext.SaveChangesAsync());

        Assert.IsType<SqlException>(exception.InnerException);
    }

    [RelationalFact]
    public async Task A_concurrent_resolution_is_rejected_by_the_database()
    {
        var containerId = await SeedProfiledReeferAsync();
        Guid excursionId;

        await using (var writeContext = CreateDbContext())
        {
            var recorded = await RecordAsync(writeContext, containerId, 6.5m);
            excursionId = recorded.Excursion!.Id;
        }

        await using var firstContext = CreateDbContext();
        await using var secondContext = CreateDbContext();

        var first = await firstContext.Set<ReeferExcursion>()
            .SingleAsync(excursion => excursion.Id == excursionId);
        var second = await secondContext.Set<ReeferExcursion>()
            .SingleAsync(excursion => excursion.Id == excursionId);

        first.Resolve(
            ReeferExcursionDisposition.CorrectiveAction,
            "Reefer unit restarted",
            ManagerUserId,
            FixedUtcNow.UtcDateTime);
        await firstContext.SaveChangesAsync();

        second.Resolve(
            ReeferExcursionDisposition.AcceptedDeviation,
            "Cargo accepted",
            OperatorUserId,
            FixedUtcNow.UtcDateTime);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondContext.SaveChangesAsync());

        await using var verifyContext = CreateDbContext();
        var stored = await verifyContext.Set<ReeferExcursion>()
            .SingleAsync(excursion => excursion.Id == excursionId);

        Assert.Equal(ReeferExcursionDisposition.CorrectiveAction, stored.Disposition);
        Assert.Equal(ManagerUserId, stored.ResolvedByUserId);
    }

    [RelationalFact]
    public async Task A_failed_save_rolls_back_the_reading_and_the_excursion_together()
    {
        var containerId = await SeedProfiledReeferAsync();

        await using (var failingContext = CreateDbContext())
        {
            var profile = await failingContext.Set<ReeferMonitoringProfile>()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.ContainerId == containerId);

            var reading = ReeferTemperatureReading.Record(
                profile,
                6.5m,
                MeasuredAtUtc,
                null,
                OperatorUserId,
                FixedUtcNow.UtcDateTime);

            var orphan = ReeferExcursion.Open(ReeferTemperatureReading.Record(
                profile,
                7m,
                MeasuredAtUtc,
                null,
                OperatorUserId,
                FixedUtcNow.UtcDateTime));

            await failingContext.Set<ReeferTemperatureReading>().AddAsync(reading);
            await failingContext.Set<ReeferExcursion>().AddAsync(orphan);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => failingContext.SaveChangesAsync());
        }

        await using var verifyContext = CreateDbContext();

        Assert.Empty(await verifyContext.Set<ReeferTemperatureReading>()
            .Where(reading => reading.ContainerId == containerId)
            .ToListAsync());
        Assert.Empty(await verifyContext.Set<ReeferExcursion>()
            .Where(excursion => excursion.ContainerId == containerId)
            .ToListAsync());
    }

    private async Task<Guid> SeedProfiledReeferAsync()
    {
        await using var dbContext = CreateDbContext();

        var container = Container.Register(
            $"MSKU{Random.Shared.Next(1000000, 9999999).ToString(CultureInfo.InvariantCulture)}",
            "42R1",
            CargoStatus.Registered);

        await dbContext.Set<Container>().AddAsync(container);
        await dbContext.SaveChangesAsync();

        await new SaveReeferMonitoringProfileService(
                dbContext,
                new FixedTimeProvider(FixedUtcNow))
            .SaveAsync(
                container.Id,
                ManagerUserId,
                new SaveReeferMonitoringProfileRequest
                {
                    MinimumCelsius = -2m,
                    MaximumCelsius = 4m,
                    TargetCelsius = 1m,
                    Reason = "Initial cold chain setup",
                },
                CancellationToken.None);

        return container.Id;
    }

    private static Task<RecordReeferTemperatureReadingResponse> RecordAsync(
        HarborOpsDbContext dbContext,
        Guid containerId,
        decimal temperatureCelsius)
    {
        return new RecordReeferTemperatureReadingService(
                dbContext,
                new FixedTimeProvider(FixedUtcNow))
            .RecordAsync(
                containerId,
                OperatorUserId,
                new RecordReeferTemperatureReadingRequest
                {
                    TemperatureCelsius = temperatureCelsius,
                    MeasuredAtUtc = MeasuredAtUtc,
                },
                CancellationToken.None);
    }

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(
        HarborOpsDbContext dbContext,
        string sql)
    {
        var results = new List<string>();

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        await dbContext.Database.OpenConnectionAsync();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (!await reader.IsDBNullAsync(0))
            {
                results.Add(reader.GetString(0));
            }
        }

        return results;
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
