using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class ReleaseContainerServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 7, 10, 30, 0, TimeSpan.Zero);

    private static readonly Guid ActorUserId =
        Guid.Parse("6f5e4d3c-2b1a-4f0e-9d8c-7b6a5f4e3d2c");

    [Fact]
    public async Task ReleaseAsync_releases_a_sealed_and_passed_container()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedAsync(dbContext, ContainerInspectionResult.Passed);

        var details = await CreateService(dbContext).ReleaseAsync(
            container.Id,
            ActorUserId,
            new ReleaseContainerRequest { Notes = "  Cleared for loading  " },
            CancellationToken.None);

        Assert.Equal(nameof(ContainerControlState.Released), details.State);
        Assert.Equal("Cleared for loading", details.StateReason);
        Assert.Equal(ActorUserId, details.StateChangedByUserId);
        Assert.Equal(FixedUtcNow.UtcDateTime, details.StateChangedAtUtc);
        Assert.False(details.CanRelease);
        Assert.Equal(
            ContainerControlTransitionNotAllowedException.AlreadyReleasedReason,
            details.ReleaseBlockedReason);
    }

    [Fact]
    public async Task ReleaseAsync_reports_no_blocker_while_a_container_is_still_releasable()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedAsync(dbContext, ContainerInspectionResult.Passed);

        var query = new GetContainerControlQuery(dbContext);
        var before = await query.GetAsync(container.Id, CancellationToken.None);

        Assert.True(before.Control.CanRelease);
        Assert.Null(before.Control.ReleaseBlockedReason);
    }

    [Fact]
    public async Task ReleaseAsync_appends_a_history_entry()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedAsync(dbContext, ContainerInspectionResult.Passed);

        await CreateService(dbContext).ReleaseAsync(
            container.Id,
            ActorUserId,
            new ReleaseContainerRequest(),
            CancellationToken.None);

        var entry = await dbContext.Set<ContainerControlEvent>()
            .SingleAsync(candidate => candidate.Action == ContainerControlAction.Released);

        Assert.Equal(3, await dbContext.Set<ContainerControlEvent>().CountAsync());
        Assert.Equal(ContainerControlState.Pending, entry.FromState);
        Assert.Equal(ContainerControlState.Released, entry.ToState);
        Assert.Equal(ActorUserId, entry.ActorUserId);
    }

    [Fact]
    public async Task ReleaseAsync_is_rejected_without_a_seal()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedContainerAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ContainerControlTransitionException>(
            () => CreateService(dbContext).ReleaseAsync(
                container.Id,
                ActorUserId,
                new ReleaseContainerRequest(),
                CancellationToken.None));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.SealNotIntactReason,
            exception.Reason);
        Assert.Empty(await dbContext.Set<ContainerControlEvent>().ToListAsync());
    }

    [Fact]
    public async Task ReleaseAsync_is_rejected_when_the_latest_inspection_failed()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedAsync(dbContext, ContainerInspectionResult.Failed);

        var exception = await Assert.ThrowsAsync<ContainerControlTransitionException>(
            () => CreateService(dbContext).ReleaseAsync(
                container.Id,
                ActorUserId,
                new ReleaseContainerRequest(),
                CancellationToken.None));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.InspectionNotPassedReason,
            exception.Reason);
    }

    [Fact]
    public async Task ReleaseAsync_is_rejected_while_the_container_is_held()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedAsync(dbContext, ContainerInspectionResult.Passed);

        var control = await dbContext.Set<ContainerControl>().SingleAsync();
        var holdEntry = control.Hold("Damaged corner post", ActorUserId, FixedUtcNow.UtcDateTime);
        await dbContext.Set<ContainerControlEvent>().AddAsync(holdEntry);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ContainerControlTransitionException>(
            () => CreateService(dbContext).ReleaseAsync(
                container.Id,
                ActorUserId,
                new ReleaseContainerRequest(),
                CancellationToken.None));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.HeldReason,
            exception.Reason);
    }

    [Fact]
    public async Task ReleaseAsync_is_rejected_when_the_container_is_already_released()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedAsync(dbContext, ContainerInspectionResult.Passed);
        var service = CreateService(dbContext);

        await service.ReleaseAsync(
            container.Id,
            ActorUserId,
            new ReleaseContainerRequest(),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ContainerControlTransitionException>(
            () => service.ReleaseAsync(
                container.Id,
                ActorUserId,
                new ReleaseContainerRequest(),
                CancellationToken.None));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.AlreadyReleasedReason,
            exception.Reason);
    }

    [Fact]
    public async Task ReleaseAsync_rejects_an_unknown_container()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<ContainerNotFoundException>(
            () => CreateService(dbContext).ReleaseAsync(
                Guid.NewGuid(),
                ActorUserId,
                new ReleaseContainerRequest(),
                CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_release_and_hold_leave_a_single_outcome()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid containerId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            containerId = (await SeedAsync(seedContext, ContainerInspectionResult.Passed)).Id;
        }

        await using var releaseContext = CreateDbContext(databaseName);
        await using var holdContext = CreateDbContext(databaseName);

        var releaseControl = await releaseContext.Set<ContainerControl>()
            .SingleAsync(control => control.ContainerId == containerId);
        var holdControl = await holdContext.Set<ContainerControl>()
            .SingleAsync(control => control.ContainerId == containerId);

        var releaseEntry = releaseControl.Release(ActorUserId, FixedUtcNow.UtcDateTime, null);
        await releaseContext.Set<ContainerControlEvent>().AddAsync(releaseEntry);
        await releaseContext.SaveChangesAsync();

        var holdEntry = holdControl.Hold("Damaged corner post", ActorUserId, FixedUtcNow.UtcDateTime);
        await holdContext.Set<ContainerControlEvent>().AddAsync(holdEntry);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => holdContext.SaveChangesAsync());

        await using var verifyContext = CreateDbContext(databaseName);
        var stored = await verifyContext.Set<ContainerControl>()
            .SingleAsync(control => control.ContainerId == containerId);

        Assert.Equal(ContainerControlState.Released, stored.State);
    }

    [Fact]
    public async Task ReleaseAsync_writes_nothing_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid containerId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            containerId = (await SeedAsync(seedContext, ContainerInspectionResult.Passed)).Id;
        }

        await using (var failingContext = CreateDbContext(databaseName, failOnSave: true))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => CreateService(failingContext).ReleaseAsync(
                    containerId,
                    ActorUserId,
                    new ReleaseContainerRequest(),
                    CancellationToken.None));
        }

        await using var verifyContext = CreateDbContext(databaseName);
        var control = await verifyContext.Set<ContainerControl>().SingleAsync();

        Assert.Equal(ContainerControlState.Pending, control.State);
        Assert.Equal(2, await verifyContext.Set<ContainerControlEvent>().CountAsync());
    }

    private static ReleaseContainerService CreateService(DbContext dbContext)
    {
        return new ReleaseContainerService(dbContext, new FixedTimeProvider(FixedUtcNow));
    }

    private static async Task<Container> SeedContainerAsync(HarborOpsDbContext dbContext)
    {
        var container = Container.Register("MSKU1234567", "40HC", CargoStatus.Registered);

        await dbContext.Set<Container>().AddAsync(container, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return container;
    }

    private static async Task<Container> SeedAsync(
        HarborOpsDbContext dbContext,
        ContainerInspectionResult result)
    {
        var container = await SeedContainerAsync(dbContext);
        var control = ContainerControl.Start(container.Id);
        var sealEntry = control.AssignSeal("ML458821", ActorUserId, FixedUtcNow.UtcDateTime);
        var inspectionEntry = control.RecordInspection(
            result,
            "Doors aligned",
            null,
            isReefer: false,
            ActorUserId,
            FixedUtcNow.UtcDateTime);

        await dbContext.Set<ContainerControl>().AddAsync(control, CancellationToken.None);
        await dbContext.Set<ContainerControlEvent>()
            .AddRangeAsync([sealEntry, .. inspectionEntry], CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

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
            throw new DbUpdateException("The save was rejected.");
        }
    }
}
