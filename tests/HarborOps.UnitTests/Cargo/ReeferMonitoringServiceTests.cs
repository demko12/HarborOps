using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class ReeferMonitoringServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 19, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LaterUtcNow =
        new(2026, 9, 19, 16, 0, 0, TimeSpan.Zero);

    private static readonly Guid ManagerUserId =
        Guid.Parse("4d5e6f70-8192-4a3b-8c4d-5e6f708192a3");

    private static readonly Guid OperatorUserId =
        Guid.Parse("1a2b3c4d-5e6f-4071-8293-a4b5c6d7e8f9");

    private static readonly DateTime MeasuredAtUtc =
        new(2026, 9, 19, 7, 45, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SaveAsync_configures_the_first_profile_and_records_the_change()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReeferAsync(dbContext);

        var details = await CreateSaveService(dbContext).SaveAsync(
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

        Assert.Equal(ReeferMonitoringProfile.InitialVersion, details.Version);
        Assert.Equal(-2m, details.MinimumCelsius);
        Assert.Equal(ManagerUserId, details.CreatedByUserId);
        Assert.Equal(FixedUtcNow.UtcDateTime, details.CreatedAtUtc);

        var change = await dbContext.Set<ReeferProfileChange>().SingleAsync();

        Assert.Null(change.FromMinimumCelsius);
        Assert.Equal(-2m, change.ToMinimumCelsius);
        Assert.Equal("Initial cold chain setup", change.Reason);
        Assert.Equal(ManagerUserId, change.ActorUserId);
    }

    [Fact]
    public async Task SaveAsync_is_rejected_for_a_container_that_is_not_a_reefer()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedContainerAsync(dbContext, "40HC");

        await Assert.ThrowsAsync<ContainerIsNotReeferException>(
            () => CreateSaveService(dbContext).SaveAsync(
                container.Id,
                ManagerUserId,
                ValidProfileRequest(),
                CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_requires_a_reason_when_an_existing_profile_changes()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ReeferMonitoringValidationException>(
            () => CreateSaveService(dbContext).SaveAsync(
                container.Id,
                ManagerUserId,
                new SaveReeferMonitoringProfileRequest
                {
                    MinimumCelsius = -5m,
                    MaximumCelsius = 2m,
                    TargetCelsius = -1m,
                },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ReeferMonitoringValidator.ReasonField));
    }

    [Fact]
    public async Task SaveAsync_revises_an_existing_profile_and_appends_a_change()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var details = await CreateSaveService(dbContext, LaterUtcNow).SaveAsync(
            container.Id,
            OperatorUserId,
            new SaveReeferMonitoringProfileRequest
            {
                MinimumCelsius = -5m,
                MaximumCelsius = 2m,
                TargetCelsius = -1m,
                Reason = "Shipper tightened the cold chain",
            },
            CancellationToken.None);

        Assert.Equal(ReeferMonitoringProfile.InitialVersion + 1, details.Version);
        Assert.Equal(OperatorUserId, details.UpdatedByUserId);
        Assert.Equal(LaterUtcNow.UtcDateTime, details.UpdatedAtUtc);

        var changes = await dbContext.Set<ReeferProfileChange>()
            .OrderBy(change => change.Version)
            .ToListAsync();

        Assert.Equal(2, changes.Count);
        Assert.Equal(-2m, changes[1].FromMinimumCelsius);
        Assert.Equal(-5m, changes[1].ToMinimumCelsius);
    }

    [Fact]
    public async Task SaveAsync_rejects_a_target_outside_the_acceptable_range()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReeferAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ReeferMonitoringValidationException>(
            () => CreateSaveService(dbContext).SaveAsync(
                container.Id,
                ManagerUserId,
                new SaveReeferMonitoringProfileRequest
                {
                    MinimumCelsius = -2m,
                    MaximumCelsius = 4m,
                    TargetCelsius = 9m,
                },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ReeferMonitoringValidator.TargetCelsiusField));
    }

    [Fact]
    public async Task RecordAsync_is_rejected_when_no_profile_is_configured()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReeferAsync(dbContext);

        await Assert.ThrowsAsync<ReeferProfileNotFoundException>(
            () => CreateRecordService(dbContext).RecordAsync(
                container.Id,
                OperatorUserId,
                ValidReadingRequest(1m),
                CancellationToken.None));

        Assert.Empty(await dbContext.Set<ReeferTemperatureReading>().ToListAsync());
    }

    [Fact]
    public async Task RecordAsync_stores_a_reading_with_the_current_threshold_snapshot()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var response = await CreateRecordService(dbContext).RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(1.5m),
            CancellationToken.None);

        Assert.Null(response.Excursion);
        Assert.Equal(nameof(ReeferReadingStatus.Effective), response.Reading.Status);
        Assert.True(response.Reading.IsWithinThresholds);
        Assert.Equal(-2m, response.Reading.ThresholdMinimumCelsius);
        Assert.Equal(4m, response.Reading.ThresholdMaximumCelsius);
        Assert.Equal(OperatorUserId, response.Reading.RecordedByUserId);
        Assert.Equal(FixedUtcNow.UtcDateTime, response.Reading.RecordedAtUtc);
        Assert.Equal(MeasuredAtUtc, response.Reading.MeasuredAtUtc);
    }

    [Fact]
    public async Task RecordAsync_opens_one_excursion_for_an_out_of_range_reading()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var response = await CreateRecordService(dbContext).RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(6.5m),
            CancellationToken.None);

        Assert.NotNull(response.Excursion);
        Assert.Equal(response.Reading.Id, response.Excursion!.ReadingId);
        Assert.Equal(nameof(ReeferExcursionDirection.Above), response.Excursion.Direction);
        Assert.Equal(nameof(ReeferExcursionStatus.Open), response.Excursion.Status);
        Assert.Single(await dbContext.Set<ReeferExcursion>().ToListAsync());
    }

    [Fact]
    public async Task RecordAsync_does_not_open_an_excursion_at_a_threshold_boundary()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var response = await CreateRecordService(dbContext).RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(4m),
            CancellationToken.None);

        Assert.Null(response.Excursion);
        Assert.Empty(await dbContext.Set<ReeferExcursion>().ToListAsync());
    }

    [Fact]
    public async Task RecordAsync_rejects_a_measurement_in_the_future()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ReeferMonitoringValidationException>(
            () => CreateRecordService(dbContext).RecordAsync(
                container.Id,
                OperatorUserId,
                new RecordReeferTemperatureReadingRequest
                {
                    TemperatureCelsius = 1m,
                    MeasuredAtUtc = FixedUtcNow.UtcDateTime.AddMinutes(1),
                },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ReeferMonitoringValidator.MeasuredAtUtcField));
    }

    [Fact]
    public async Task RecordAsync_rejects_a_temperature_beyond_the_supported_precision()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ReeferMonitoringValidationException>(
            () => CreateRecordService(dbContext).RecordAsync(
                container.Id,
                OperatorUserId,
                ValidReadingRequest(1.555m),
                CancellationToken.None));

        Assert.True(
            exception.Errors.ContainsKey(ReeferMonitoringValidator.TemperatureCelsiusField));
    }

    [Fact]
    public async Task GetAsync_pages_readings_newest_measurement_first()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);
        var service = CreateRecordService(dbContext);

        foreach (var offset in new[] { -3, -1, -2 })
        {
            await service.RecordAsync(
                container.Id,
                OperatorUserId,
                new RecordReeferTemperatureReadingRequest
                {
                    TemperatureCelsius = 1m,
                    MeasuredAtUtc = MeasuredAtUtc.AddHours(offset),
                },
                CancellationToken.None);
        }

        var page = await new GetReeferTemperatureReadingsQuery(dbContext).GetAsync(
            container.Id,
            new GetReeferTemperatureReadingsRequest { PageNumber = 1, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(MeasuredAtUtc.AddHours(-1), page.Items[0].MeasuredAtUtc);
        Assert.Equal(MeasuredAtUtc.AddHours(-2), page.Items[1].MeasuredAtUtc);
    }

    [Fact]
    public async Task GetAsync_rejects_an_oversized_page()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ReeferMonitoringValidationException>(
            () => new GetReeferTemperatureReadingsQuery(dbContext).GetAsync(
                container.Id,
                new GetReeferTemperatureReadingsRequest
                {
                    PageSize = ReeferMonitoringValidator.MaxPageSize + 1,
                },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ReeferMonitoringValidator.PageSizeField));
    }

    [Fact]
    public async Task CorrectAsync_supersedes_the_original_reading_and_its_excursion()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var recorded = await CreateRecordService(dbContext).RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(6.5m),
            CancellationToken.None);

        var corrected = await CreateCorrectService(dbContext, LaterUtcNow).CorrectAsync(
            container.Id,
            recorded.Reading.Id,
            ManagerUserId,
            new CorrectReeferTemperatureReadingRequest
            {
                TemperatureCelsius = 2m,
                MeasuredAtUtc = MeasuredAtUtc,
                Reason = "Probe was misread",
            },
            CancellationToken.None);

        Assert.Null(corrected.Excursion);
        Assert.Equal(recorded.Reading.Id, corrected.Reading.CorrectsReadingId);

        var original = await dbContext.Set<ReeferTemperatureReading>()
            .SingleAsync(reading => reading.Id == recorded.Reading.Id);

        Assert.Equal(ReeferReadingStatus.Superseded, original.Status);
        Assert.Equal("Probe was misread", original.CorrectionReason);
        Assert.Equal(ManagerUserId, original.CorrectedByUserId);
        Assert.Equal(6.5m, original.TemperatureCelsius);

        var excursion = await dbContext.Set<ReeferExcursion>().SingleAsync();

        Assert.Equal(ReeferExcursionStatus.Superseded, excursion.Status);
        Assert.Null(excursion.SupersededByExcursionId);
        Assert.Equal(LaterUtcNow.UtcDateTime, excursion.SupersededAtUtc);
    }

    [Fact]
    public async Task CorrectAsync_evaluates_against_the_original_threshold_snapshot()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var recorded = await CreateRecordService(dbContext).RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(6.5m),
            CancellationToken.None);

        await CreateSaveService(dbContext, LaterUtcNow).SaveAsync(
            container.Id,
            ManagerUserId,
            new SaveReeferMonitoringProfileRequest
            {
                MinimumCelsius = -20m,
                MaximumCelsius = 20m,
                TargetCelsius = 0m,
                Reason = "Range widened",
            },
            CancellationToken.None);

        var corrected = await CreateCorrectService(dbContext, LaterUtcNow).CorrectAsync(
            container.Id,
            recorded.Reading.Id,
            ManagerUserId,
            new CorrectReeferTemperatureReadingRequest
            {
                TemperatureCelsius = 5m,
                MeasuredAtUtc = MeasuredAtUtc,
                Reason = "Probe was misread",
            },
            CancellationToken.None);

        Assert.Equal(-2m, corrected.Reading.ThresholdMinimumCelsius);
        Assert.Equal(4m, corrected.Reading.ThresholdMaximumCelsius);
        Assert.NotNull(corrected.Excursion);

        var replacementExcursion = await dbContext.Set<ReeferExcursion>()
            .SingleAsync(excursion => excursion.Status == ReeferExcursionStatus.Open);
        var supersededExcursion = await dbContext.Set<ReeferExcursion>()
            .SingleAsync(excursion => excursion.Status == ReeferExcursionStatus.Superseded);

        Assert.Equal(replacementExcursion.Id, supersededExcursion.SupersededByExcursionId);
    }

    [Fact]
    public async Task CorrectAsync_is_rejected_once_a_reading_is_superseded()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var recorded = await CreateRecordService(dbContext).RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(6.5m),
            CancellationToken.None);

        var request = new CorrectReeferTemperatureReadingRequest
        {
            TemperatureCelsius = 2m,
            MeasuredAtUtc = MeasuredAtUtc,
            Reason = "Probe was misread",
        };

        await CreateCorrectService(dbContext).CorrectAsync(
            container.Id,
            recorded.Reading.Id,
            ManagerUserId,
            request,
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ReeferReadingTransitionException>(
            () => CreateCorrectService(dbContext).CorrectAsync(
                container.Id,
                recorded.Reading.Id,
                ManagerUserId,
                request,
                CancellationToken.None));

        Assert.Equal(ReeferReadingStatus.Superseded, exception.CurrentStatus);
    }

    [Fact]
    public async Task VoidAsync_voids_the_reading_and_supersedes_its_excursion()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var recorded = await CreateRecordService(dbContext).RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(6.5m),
            CancellationToken.None);

        var voided = await CreateVoidService(dbContext, LaterUtcNow).VoidAsync(
            container.Id,
            recorded.Reading.Id,
            ManagerUserId,
            new VoidReeferTemperatureReadingRequest { Reason = "Duplicate entry" },
            CancellationToken.None);

        Assert.Equal(nameof(ReeferReadingStatus.Voided), voided.Status);
        Assert.Equal("Duplicate entry", voided.VoidReason);
        Assert.Equal(ManagerUserId, voided.VoidedByUserId);
        Assert.Equal(LaterUtcNow.UtcDateTime, voided.VoidedAtUtc);

        var excursion = await dbContext.Set<ReeferExcursion>().SingleAsync();

        Assert.Equal(ReeferExcursionStatus.Superseded, excursion.Status);
    }

    [Fact]
    public async Task VoidAsync_requires_a_reason()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var recorded = await CreateRecordService(dbContext).RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(1m),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ReeferMonitoringValidationException>(
            () => CreateVoidService(dbContext).VoidAsync(
                container.Id,
                recorded.Reading.Id,
                ManagerUserId,
                new VoidReeferTemperatureReadingRequest { Reason = "   " },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ReeferMonitoringValidator.ReasonField));
    }

    [Fact]
    public async Task ResolveAsync_closes_an_excursion_with_a_disposition_and_explanation()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var recorded = await CreateRecordService(dbContext).RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(6.5m),
            CancellationToken.None);

        var resolved = await CreateResolveService(dbContext, LaterUtcNow).ResolveAsync(
            container.Id,
            recorded.Excursion!.Id,
            ManagerUserId,
            new ResolveReeferExcursionRequest
            {
                Disposition = nameof(ReeferExcursionDisposition.CorrectiveAction),
                Explanation = "Reefer unit restarted and verified",
            },
            CancellationToken.None);

        Assert.Equal(nameof(ReeferExcursionStatus.Resolved), resolved.Status);
        Assert.Equal(
            nameof(ReeferExcursionDisposition.CorrectiveAction),
            resolved.Disposition);
        Assert.Equal("Reefer unit restarted and verified", resolved.Explanation);
        Assert.Equal(ManagerUserId, resolved.ResolvedByUserId);
        Assert.Equal(LaterUtcNow.UtcDateTime, resolved.ResolvedAtUtc);
    }

    [Fact]
    public async Task ResolveAsync_requires_an_explanation()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var recorded = await CreateRecordService(dbContext).RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(6.5m),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ReeferMonitoringValidationException>(
            () => CreateResolveService(dbContext).ResolveAsync(
                container.Id,
                recorded.Excursion!.Id,
                ManagerUserId,
                new ResolveReeferExcursionRequest
                {
                    Disposition = nameof(ReeferExcursionDisposition.AcceptedDeviation),
                },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ReeferMonitoringValidator.ExplanationField));
    }

    [Fact]
    public async Task ResolveAsync_is_rejected_once_an_excursion_is_closed()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);

        var recorded = await CreateRecordService(dbContext).RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(6.5m),
            CancellationToken.None);

        var request = new ResolveReeferExcursionRequest
        {
            Disposition = nameof(ReeferExcursionDisposition.AcceptedDeviation),
            Explanation = "Cargo inspected and accepted",
        };

        await CreateResolveService(dbContext).ResolveAsync(
            container.Id,
            recorded.Excursion!.Id,
            ManagerUserId,
            request,
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ReeferExcursionTransitionException>(
            () => CreateResolveService(dbContext).ResolveAsync(
                container.Id,
                recorded.Excursion.Id,
                ManagerUserId,
                request,
                CancellationToken.None));

        Assert.Equal(ReeferExcursionStatus.Resolved, exception.CurrentStatus);
    }

    [Fact]
    public async Task Several_out_of_range_readings_open_several_excursions()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedProfiledReeferAsync(dbContext);
        var service = CreateRecordService(dbContext);

        await service.RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(6.5m),
            CancellationToken.None);
        await service.RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(-9m),
            CancellationToken.None);
        await service.RecordAsync(
            container.Id,
            OperatorUserId,
            ValidReadingRequest(1m),
            CancellationToken.None);

        var excursions = await new GetReeferExcursionsQuery(dbContext).GetAsync(
            container.Id,
            CancellationToken.None);

        Assert.Equal(2, excursions.Items.Count);
        Assert.Equal(2, excursions.OpenCount);
    }

    [Fact]
    public async Task RecordAsync_writes_nothing_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid containerId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            containerId = (await SeedProfiledReeferAsync(seedContext)).Id;
        }

        await using (var failingContext = CreateDbContext(databaseName, failOnSave: true))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => CreateRecordService(failingContext).RecordAsync(
                    containerId,
                    OperatorUserId,
                    ValidReadingRequest(6.5m),
                    CancellationToken.None));
        }

        await using var verifyContext = CreateDbContext(databaseName);

        Assert.Empty(await verifyContext.Set<ReeferTemperatureReading>().ToListAsync());
        Assert.Empty(await verifyContext.Set<ReeferExcursion>().ToListAsync());
    }

    [Fact]
    public async Task Concurrent_resolutions_leave_a_single_outcome()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid containerId;
        Guid excursionId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            containerId = (await SeedProfiledReeferAsync(seedContext)).Id;

            var recorded = await CreateRecordService(seedContext).RecordAsync(
                containerId,
                OperatorUserId,
                ValidReadingRequest(6.5m),
                CancellationToken.None);

            excursionId = recorded.Excursion!.Id;
        }

        await using var firstContext = CreateDbContext(databaseName);
        await using var secondContext = CreateDbContext(databaseName);

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
            LaterUtcNow.UtcDateTime);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondContext.SaveChangesAsync());

        await using var verifyContext = CreateDbContext(databaseName);
        var stored = await verifyContext.Set<ReeferExcursion>().SingleAsync();

        Assert.Equal(ReeferExcursionDisposition.CorrectiveAction, stored.Disposition);
        Assert.Equal(ManagerUserId, stored.ResolvedByUserId);
    }

    private static SaveReeferMonitoringProfileRequest ValidProfileRequest()
    {
        return new SaveReeferMonitoringProfileRequest
        {
            MinimumCelsius = -2m,
            MaximumCelsius = 4m,
            TargetCelsius = 1m,
            Reason = "Initial cold chain setup",
        };
    }

    private static RecordReeferTemperatureReadingRequest ValidReadingRequest(
        decimal temperatureCelsius)
    {
        return new RecordReeferTemperatureReadingRequest
        {
            TemperatureCelsius = temperatureCelsius,
            MeasuredAtUtc = MeasuredAtUtc,
        };
    }

    private static SaveReeferMonitoringProfileService CreateSaveService(
        DbContext dbContext,
        DateTimeOffset? utcNow = null)
    {
        return new SaveReeferMonitoringProfileService(
            dbContext,
            new FixedTimeProvider(utcNow ?? FixedUtcNow));
    }

    private static RecordReeferTemperatureReadingService CreateRecordService(
        DbContext dbContext,
        DateTimeOffset? utcNow = null)
    {
        return new RecordReeferTemperatureReadingService(
            dbContext,
            new FixedTimeProvider(utcNow ?? FixedUtcNow));
    }

    private static CorrectReeferTemperatureReadingService CreateCorrectService(
        DbContext dbContext,
        DateTimeOffset? utcNow = null)
    {
        return new CorrectReeferTemperatureReadingService(
            dbContext,
            new FixedTimeProvider(utcNow ?? FixedUtcNow));
    }

    private static VoidReeferTemperatureReadingService CreateVoidService(
        DbContext dbContext,
        DateTimeOffset? utcNow = null)
    {
        return new VoidReeferTemperatureReadingService(
            dbContext,
            new FixedTimeProvider(utcNow ?? FixedUtcNow));
    }

    private static ResolveReeferExcursionService CreateResolveService(
        DbContext dbContext,
        DateTimeOffset? utcNow = null)
    {
        return new ResolveReeferExcursionService(
            dbContext,
            new FixedTimeProvider(utcNow ?? FixedUtcNow));
    }

    private static async Task<Container> SeedContainerAsync(
        HarborOpsDbContext dbContext,
        string containerType)
    {
        var container = Container.Register("MSKU1234567", containerType, CargoStatus.Registered);

        await dbContext.Set<Container>().AddAsync(container, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return container;
    }

    private static Task<Container> SeedReeferAsync(HarborOpsDbContext dbContext)
    {
        return SeedContainerAsync(dbContext, "42R1");
    }

    private static async Task<Container> SeedProfiledReeferAsync(HarborOpsDbContext dbContext)
    {
        var container = await SeedReeferAsync(dbContext);

        await CreateSaveService(dbContext).SaveAsync(
            container.Id,
            ManagerUserId,
            ValidProfileRequest(),
            CancellationToken.None);

        return container;
    }

    private static HarborOpsDbContext CreateDbContext(
        string? databaseName = null,
        bool failOnSave = false)
    {
        var builder = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}");

        if (failOnSave)
        {
            builder.AddInterceptors(new FailingSaveChangesInterceptor());
        }

        return new HarborOpsDbContext(builder.Options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }

    private sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException("Simulated save failure.");
        }
    }
}
