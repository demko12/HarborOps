using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class ReeferMonitoringGateTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 19, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LaterUtcNow =
        new(2026, 9, 19, 16, 0, 0, TimeSpan.Zero);

    private static readonly Guid ManagerUserId =
        Guid.Parse("4d5e6f70-8192-4a3b-8c4d-5e6f708192a3");

    private static readonly Guid OperatorUserId =
        Guid.Parse("1a2b3c4d-5e6f-4071-8293-a4b5c6d7e8f9");

    private static readonly Guid CargoBookingId =
        Guid.Parse("b7c8d9e0-f102-4314-8526-738495a6b7c8");

    private static readonly DateTime MeasuredAtUtc =
        new(2026, 9, 19, 7, 45, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_open_excursion_blocks_releasing_a_container()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasableReeferAsync(dbContext);
        await RecordBreachAsync(dbContext, container.Id);

        var exception = await Assert.ThrowsAsync<ContainerControlTransitionException>(
            () => CreateReleaseService(dbContext).ReleaseAsync(
                container.Id,
                ManagerUserId,
                new ReleaseContainerRequest(),
                CancellationToken.None));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.OpenTemperatureExcursionReason,
            exception.Reason);
    }

    [Fact]
    public async Task An_open_excursion_is_reported_as_the_release_blocker()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasableReeferAsync(dbContext);
        await RecordBreachAsync(dbContext, container.Id);

        var response = await new GetContainerControlQuery(dbContext).GetAsync(
            container.Id,
            CancellationToken.None);

        Assert.False(response.Control.CanRelease);
        Assert.Equal(
            ContainerControlTransitionNotAllowedException.OpenTemperatureExcursionReason,
            response.Control.ReleaseBlockedReason);
    }

    [Fact]
    public async Task A_passing_inspection_does_not_clear_an_open_excursion()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasableReeferAsync(dbContext);
        await RecordBreachAsync(dbContext, container.Id);

        var details = await new RecordContainerInspectionService(
                dbContext,
                new FixedTimeProvider(LaterUtcNow))
            .RecordAsync(
                container.Id,
                ManagerUserId,
                new RecordContainerInspectionRequest
                {
                    Result = nameof(ContainerInspectionResult.Passed),
                    ConditionNotes = "Doors aligned",
                },
                CancellationToken.None);

        Assert.False(details.CanRelease);
        Assert.Equal(
            ContainerControlTransitionNotAllowedException.OpenTemperatureExcursionReason,
            details.ReleaseBlockedReason);

        var excursion = await dbContext.Set<ReeferExcursion>().SingleAsync();

        Assert.Equal(ReeferExcursionStatus.Open, excursion.Status);
    }

    [Fact]
    public async Task Resolving_an_excursion_unblocks_a_release_without_performing_it()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasableReeferAsync(dbContext);
        var excursionId = await RecordBreachAsync(dbContext, container.Id);

        await ResolveAsync(dbContext, container.Id, excursionId);

        var response = await new GetContainerControlQuery(dbContext).GetAsync(
            container.Id,
            CancellationToken.None);

        Assert.Equal(nameof(ContainerControlState.Pending), response.Control.State);
        Assert.True(response.Control.CanRelease);
        Assert.Null(response.Control.ReleaseBlockedReason);
    }

    [Fact]
    public async Task Resolving_an_excursion_does_not_lift_a_hold()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasableReeferAsync(dbContext);
        var excursionId = await RecordBreachAsync(dbContext, container.Id);

        await new HoldContainerService(dbContext, new FixedTimeProvider(FixedUtcNow)).HoldAsync(
            container.Id,
            ManagerUserId,
            new HoldContainerRequest { Reason = "Damaged corner post" },
            CancellationToken.None);

        await ResolveAsync(dbContext, container.Id, excursionId);

        var response = await new GetContainerControlQuery(dbContext).GetAsync(
            container.Id,
            CancellationToken.None);

        Assert.Equal(nameof(ContainerControlState.Held), response.Control.State);
        Assert.Equal("Damaged corner post", response.Control.StateReason);
        Assert.False(response.Control.CanRelease);
        Assert.Equal(
            ContainerControlTransitionNotAllowedException.HeldReason,
            response.Control.ReleaseBlockedReason);
    }

    [Fact]
    public async Task Voiding_the_breaching_reading_unblocks_a_release()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasableReeferAsync(dbContext);
        await RecordBreachAsync(dbContext, container.Id);

        var reading = await dbContext.Set<ReeferTemperatureReading>().SingleAsync();

        await new VoidReeferTemperatureReadingService(
                dbContext,
                new FixedTimeProvider(LaterUtcNow))
            .VoidAsync(
                container.Id,
                reading.Id,
                ManagerUserId,
                new VoidReeferTemperatureReadingRequest { Reason = "Duplicate entry" },
                CancellationToken.None);

        var details = await CreateReleaseService(dbContext).ReleaseAsync(
            container.Id,
            ManagerUserId,
            new ReleaseContainerRequest(),
            CancellationToken.None);

        Assert.Equal(nameof(ContainerControlState.Released), details.State);
    }

    [Fact]
    public async Task An_open_excursion_blocks_loading_a_previously_released_container()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasableReeferAsync(dbContext);

        await CreateReleaseService(dbContext).ReleaseAsync(
            container.Id,
            ManagerUserId,
            new ReleaseContainerRequest(),
            CancellationToken.None);

        await ContainerControlGuard.EnsureContainersAreReleasedAsync(
            dbContext,
            CargoBookingId,
            CancellationToken.None);

        await RecordBreachAsync(dbContext, container.Id);

        var exception = await Assert.ThrowsAsync<CargoContainersNotReleasedException>(
            () => ContainerControlGuard.EnsureContainersAreReleasedAsync(
                dbContext,
                CargoBookingId,
                CancellationToken.None));

        Assert.Contains("MSKU1234567", exception.ContainerNumbers);
    }

    [Fact]
    public async Task Loading_is_allowed_again_once_the_excursion_is_resolved()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasableReeferAsync(dbContext);

        await CreateReleaseService(dbContext).ReleaseAsync(
            container.Id,
            ManagerUserId,
            new ReleaseContainerRequest(),
            CancellationToken.None);

        var excursionId = await RecordBreachAsync(dbContext, container.Id);
        await ResolveAsync(dbContext, container.Id, excursionId);

        await ContainerControlGuard.EnsureContainersAreReleasedAsync(
            dbContext,
            CargoBookingId,
            CancellationToken.None);

        var control = await dbContext.Set<ContainerControl>().SingleAsync();

        Assert.Equal(ContainerControlState.Released, control.State);
    }

    [Fact]
    public async Task A_reading_on_a_discharged_container_does_not_undo_its_status()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasableReeferAsync(dbContext);

        container.ApplyOperationOutcome(CargoOperationType.Unload);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await RecordBreachAsync(dbContext, container.Id);

        var stored = await dbContext.Set<Container>().SingleAsync();

        Assert.Equal(CargoStatus.Unloaded, stored.Status);
        Assert.Single(await dbContext.Set<ReeferExcursion>().ToListAsync());
    }

    [Fact]
    public async Task A_reading_that_lands_during_a_release_still_blocks_the_load_start()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid containerId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            containerId = (await SeedReleasableReeferAsync(seedContext)).Id;
        }

        await using var releaseContext = CreateDbContext(databaseName);
        await using var readingContext = CreateDbContext(databaseName);

        var control = await releaseContext.Set<ContainerControl>()
            .SingleAsync(candidate => candidate.ContainerId == containerId);

        var hasOpenExcursion = await ReeferMonitoringGuard.HasOpenExcursionAsync(
            releaseContext,
            containerId,
            CancellationToken.None);

        Assert.False(hasOpenExcursion);

        await RecordBreachAsync(readingContext, containerId);

        var entry = control.Release(
            ManagerUserId,
            LaterUtcNow.UtcDateTime,
            null,
            hasOpenExcursion);
        await releaseContext.Set<ContainerControlEvent>().AddAsync(entry, CancellationToken.None);
        await releaseContext.SaveChangesAsync(CancellationToken.None);

        await using var verifyContext = CreateDbContext(databaseName);
        var stored = await verifyContext.Set<ContainerControl>()
            .SingleAsync(candidate => candidate.ContainerId == containerId);

        Assert.Equal(ContainerControlState.Released, stored.State);

        await Assert.ThrowsAsync<CargoContainersNotReleasedException>(
            () => ContainerControlGuard.EnsureContainersAreReleasedAsync(
                verifyContext,
                CargoBookingId,
                CancellationToken.None));
    }

    [Fact]
    public async Task The_profile_view_reports_the_latest_inspection_temperature()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasableReeferAsync(dbContext);

        await new RecordContainerInspectionService(
                dbContext,
                new FixedTimeProvider(LaterUtcNow))
            .RecordAsync(
                container.Id,
                ManagerUserId,
                new RecordContainerInspectionRequest
                {
                    Result = nameof(ContainerInspectionResult.Passed),
                    ConditionNotes = "Doors aligned",
                    TemperatureCelsius = 9m,
                },
                CancellationToken.None);

        var view = await new GetReeferMonitoringProfileQuery(dbContext)
            .GetAsync(container.Id, CancellationToken.None);

        Assert.NotNull(view.Profile);
        Assert.Equal(9m, view.LatestInspectionTemperatureCelsius);
        Assert.True(view.LatestInspectionTemperatureCelsius > view.Profile!.MaximumCelsius);
    }

    private static async Task<Guid> RecordBreachAsync(HarborOpsDbContext dbContext, Guid containerId)
    {
        var response = await new RecordReeferTemperatureReadingService(
                dbContext,
                new FixedTimeProvider(FixedUtcNow))
            .RecordAsync(
                containerId,
                OperatorUserId,
                new RecordReeferTemperatureReadingRequest
                {
                    TemperatureCelsius = 6.5m,
                    MeasuredAtUtc = MeasuredAtUtc,
                },
                CancellationToken.None);

        return response.Excursion!.Id;
    }

    private static Task ResolveAsync(
        HarborOpsDbContext dbContext,
        Guid containerId,
        Guid excursionId)
    {
        return new ResolveReeferExcursionService(dbContext, new FixedTimeProvider(LaterUtcNow))
            .ResolveAsync(
                containerId,
                excursionId,
                ManagerUserId,
                new ResolveReeferExcursionRequest
                {
                    Disposition = nameof(ReeferExcursionDisposition.CorrectiveAction),
                    Explanation = "Reefer unit restarted and verified",
                },
                CancellationToken.None);
    }

    private static ReleaseContainerService CreateReleaseService(DbContext dbContext)
    {
        return new ReleaseContainerService(dbContext, new FixedTimeProvider(LaterUtcNow));
    }

    private static async Task<Container> SeedReleasableReeferAsync(HarborOpsDbContext dbContext)
    {
        var container = Container.Register(
            "MSKU1234567",
            "42R1",
            CargoStatus.Registered,
            cargoBookingId: CargoBookingId);

        var control = ContainerControl.Start(container.Id);
        var sealEntry = control.AssignSeal("ML458821", OperatorUserId, FixedUtcNow.UtcDateTime);
        var inspectionEntries = control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Doors aligned",
            null,
            isReefer: true,
            OperatorUserId,
            FixedUtcNow.UtcDateTime);

        await dbContext.Set<Container>().AddAsync(container, CancellationToken.None);
        await dbContext.Set<ContainerControl>().AddAsync(control, CancellationToken.None);
        await dbContext.Set<ContainerControlEvent>()
            .AddRangeAsync([sealEntry, .. inspectionEntries], CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

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

        return container;
    }

    private static HarborOpsDbContext CreateDbContext(string? databaseName = null)
    {
        var builder = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}");

        return new HarborOpsDbContext(builder.Options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
