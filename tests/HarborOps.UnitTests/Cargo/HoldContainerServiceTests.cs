using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class HoldContainerServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 7, 11, 45, 0, TimeSpan.Zero);

    private static readonly Guid ActorUserId =
        Guid.Parse("7a6f5e4d-3c2b-4a1f-8e9d-0c7b6a5f4e3d");

    [Fact]
    public async Task HoldAsync_stores_the_reason_and_the_actor()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasedAsync(dbContext);

        var details = await CreateService(dbContext).HoldAsync(
            container.Id,
            ActorUserId,
            new HoldContainerRequest { Reason = "  Damaged corner post  " },
            CancellationToken.None);

        Assert.Equal(nameof(ContainerControlState.Held), details.State);
        Assert.Equal("Damaged corner post", details.StateReason);
        Assert.Equal(ActorUserId, details.StateChangedByUserId);
        Assert.Equal(FixedUtcNow.UtcDateTime, details.StateChangedAtUtc);
    }

    [Fact]
    public async Task HoldAsync_removes_release_eligibility_immediately()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasedAsync(dbContext);

        var details = await CreateService(dbContext).HoldAsync(
            container.Id,
            ActorUserId,
            new HoldContainerRequest { Reason = "Damaged corner post" },
            CancellationToken.None);

        Assert.False(details.CanRelease);
        Assert.Equal(
            ContainerControlTransitionNotAllowedException.HeldReason,
            details.ReleaseBlockedReason);
    }

    [Fact]
    public async Task HoldAsync_appends_a_history_entry()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasedAsync(dbContext);

        await CreateService(dbContext).HoldAsync(
            container.Id,
            ActorUserId,
            new HoldContainerRequest { Reason = "Damaged corner post" },
            CancellationToken.None);

        var entry = await dbContext.Set<ContainerControlEvent>()
            .SingleAsync(candidate => candidate.Action == ContainerControlAction.Held);

        Assert.Equal(ContainerControlState.Released, entry.FromState);
        Assert.Equal(ContainerControlState.Held, entry.ToState);
        Assert.Equal("Damaged corner post", entry.Notes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HoldAsync_rejects_a_missing_reason(string? reason)
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasedAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ContainerControlValidationException>(
            () => CreateService(dbContext).HoldAsync(
                container.Id,
                ActorUserId,
                new HoldContainerRequest { Reason = reason },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ContainerControlValidator.ReasonField));

        var control = await dbContext.Set<ContainerControl>().SingleAsync();
        Assert.Equal(ContainerControlState.Released, control.State);
    }

    [Fact]
    public async Task HoldAsync_rejects_a_reason_that_is_too_long()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasedAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ContainerControlValidationException>(
            () => CreateService(dbContext).HoldAsync(
                container.Id,
                ActorUserId,
                new HoldContainerRequest
                {
                    Reason = new string('x', ContainerControl.ReasonMaxLength + 1),
                },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ContainerControlValidator.ReasonField));
    }

    [Fact]
    public async Task HoldAsync_is_rejected_when_the_container_is_already_held()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasedAsync(dbContext);
        var service = CreateService(dbContext);

        await service.HoldAsync(
            container.Id,
            ActorUserId,
            new HoldContainerRequest { Reason = "Damaged corner post" },
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ContainerControlTransitionException>(
            () => service.HoldAsync(
                container.Id,
                ActorUserId,
                new HoldContainerRequest { Reason = "Still damaged" },
                CancellationToken.None));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.AlreadyHeldReason,
            exception.Reason);
    }

    [Fact]
    public async Task HoldAsync_can_hold_a_container_that_has_no_control_yet()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedContainerAsync(dbContext);

        var details = await CreateService(dbContext).HoldAsync(
            container.Id,
            ActorUserId,
            new HoldContainerRequest { Reason = "Missing paperwork" },
            CancellationToken.None);

        Assert.Equal(nameof(ContainerControlState.Held), details.State);
        Assert.Equal(1, await dbContext.Set<ContainerControlEvent>().CountAsync());
    }

    [Fact]
    public async Task HoldAsync_rejects_an_unknown_container()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<ContainerNotFoundException>(
            () => CreateService(dbContext).HoldAsync(
                Guid.NewGuid(),
                ActorUserId,
                new HoldContainerRequest { Reason = "Missing paperwork" },
                CancellationToken.None));
    }

    [Fact]
    public async Task HoldAsync_writes_nothing_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid containerId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            containerId = (await SeedReleasedAsync(seedContext)).Id;
        }

        await using (var failingContext = CreateDbContext(databaseName, failOnSave: true))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => CreateService(failingContext).HoldAsync(
                    containerId,
                    ActorUserId,
                    new HoldContainerRequest { Reason = "Damaged corner post" },
                    CancellationToken.None));
        }

        await using var verifyContext = CreateDbContext(databaseName);
        var control = await verifyContext.Set<ContainerControl>().SingleAsync();

        Assert.Equal(ContainerControlState.Released, control.State);
        Assert.Equal(3, await verifyContext.Set<ContainerControlEvent>().CountAsync());
    }

    private static HoldContainerService CreateService(DbContext dbContext)
    {
        return new HoldContainerService(dbContext, new FixedTimeProvider(FixedUtcNow));
    }

    private static async Task<Container> SeedContainerAsync(HarborOpsDbContext dbContext)
    {
        var container = Container.Register("MSKU1234567", "40HC", CargoStatus.Registered);

        await dbContext.Set<Container>().AddAsync(container, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return container;
    }

    private static async Task<Container> SeedReleasedAsync(HarborOpsDbContext dbContext)
    {
        var container = await SeedContainerAsync(dbContext);
        var control = ContainerControl.Start(container.Id);
        var sealEntry = control.AssignSeal("ML458821", ActorUserId, FixedUtcNow.UtcDateTime);
        var inspectionEntry = control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Doors aligned",
            null,
            isReefer: false,
            ActorUserId,
            FixedUtcNow.UtcDateTime);
        var releaseEntry = control.Release(ActorUserId, FixedUtcNow.UtcDateTime, null, false);

        await dbContext.Set<ContainerControl>().AddAsync(control, CancellationToken.None);
        await dbContext.Set<ContainerControlEvent>()
            .AddRangeAsync([sealEntry, .. inspectionEntry, releaseEntry], CancellationToken.None);
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
