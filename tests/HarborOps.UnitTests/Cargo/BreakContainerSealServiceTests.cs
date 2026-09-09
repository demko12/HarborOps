using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class BreakContainerSealServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 8, 9, 30, 0, TimeSpan.Zero);

    private static readonly Guid ActorUserId =
        Guid.Parse("6b5a4938-2716-4a5b-9c8d-0e1f2a3b4c5d");

    [Fact]
    public async Task BreakAsync_marks_the_seal_broken()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedSealedContainerAsync(dbContext);

        var details = await CreateService(dbContext).BreakAsync(
            container.Id,
            ActorUserId,
            new BreakContainerSealRequest { Notes = "  Opened for customs  " },
            CancellationToken.None);

        Assert.Equal(nameof(ContainerSealStatus.Broken), details.SealStatus);
        Assert.Equal("ML458821", details.SealNumber);
        Assert.False(details.CanRelease);
    }

    [Fact]
    public async Task BreakAsync_appends_a_history_entry_with_the_trimmed_notes()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedSealedContainerAsync(dbContext);

        await CreateService(dbContext).BreakAsync(
            container.Id,
            ActorUserId,
            new BreakContainerSealRequest { Notes = "  Opened for customs  " },
            CancellationToken.None);

        var entry = await dbContext.Set<ContainerControlEvent>()
            .SingleAsync(candidate => candidate.Action == ContainerControlAction.SealBroken);

        Assert.Equal("Opened for customs", entry.Notes);
        Assert.Equal("ML458821", entry.SealNumber);
        Assert.Equal(ActorUserId, entry.ActorUserId);
        Assert.Equal(FixedUtcNow.UtcDateTime, entry.OccurredUtc);
    }

    [Fact]
    public async Task BreakAsync_withdraws_an_existing_release()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasedAsync(dbContext);

        var details = await CreateService(dbContext).BreakAsync(
            container.Id,
            ActorUserId,
            new BreakContainerSealRequest(),
            CancellationToken.None);

        Assert.Equal(nameof(ContainerControlState.Pending), details.State);
        Assert.False(details.CanRelease);
    }

    [Fact]
    public async Task BreakAsync_frees_the_seal_number_for_another_container()
    {
        await using var dbContext = CreateDbContext();
        var first = await SeedSealedContainerAsync(dbContext);
        var second = await SeedContainerAsync(dbContext, "MSKU7654321");

        await CreateService(dbContext).BreakAsync(
            first.Id,
            ActorUserId,
            new BreakContainerSealRequest(),
            CancellationToken.None);

        var details = await CreateSealService(dbContext).AssignAsync(
            second.Id,
            ActorUserId,
            new AssignContainerSealRequest { SealNumber = "ML458821" },
            CancellationToken.None);

        Assert.Equal("ML458821", details.SealNumber);
    }

    [Fact]
    public async Task BreakAsync_is_rejected_when_no_seal_was_assigned()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedContainerAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ContainerControlTransitionException>(
            () => CreateService(dbContext).BreakAsync(
                container.Id,
                ActorUserId,
                new BreakContainerSealRequest(),
                CancellationToken.None));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.SealNotIntactReason,
            exception.Reason);
    }

    [Fact]
    public async Task BreakAsync_is_rejected_when_the_seal_is_already_broken()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedSealedContainerAsync(dbContext);
        var service = CreateService(dbContext);

        await service.BreakAsync(
            container.Id,
            ActorUserId,
            new BreakContainerSealRequest(),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ContainerControlTransitionException>(
            () => service.BreakAsync(
                container.Id,
                ActorUserId,
                new BreakContainerSealRequest(),
                CancellationToken.None));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.SealAlreadyBrokenReason,
            exception.Reason);
    }

    [Fact]
    public async Task BreakAsync_rejects_notes_that_are_too_long()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedSealedContainerAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ContainerControlValidationException>(
            () => CreateService(dbContext).BreakAsync(
                container.Id,
                ActorUserId,
                new BreakContainerSealRequest { Notes = new string('x', 501) },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ContainerControlValidator.NotesField));
    }

    [Fact]
    public async Task BreakAsync_rejects_an_unknown_container()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<ContainerNotFoundException>(
            () => CreateService(dbContext).BreakAsync(
                Guid.NewGuid(),
                ActorUserId,
                new BreakContainerSealRequest(),
                CancellationToken.None));
    }

    [Fact]
    public async Task BreakAsync_writes_nothing_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid containerId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            containerId = (await SeedSealedContainerAsync(seedContext)).Id;
        }

        await using (var failingContext = CreateDbContext(databaseName, failOnSave: true))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => CreateService(failingContext).BreakAsync(
                    containerId,
                    ActorUserId,
                    new BreakContainerSealRequest(),
                    CancellationToken.None));
        }

        await using var verifyContext = CreateDbContext(databaseName);
        var control = await verifyContext.Set<ContainerControl>()
            .SingleAsync(candidate => candidate.ContainerId == containerId);

        Assert.Equal(ContainerSealStatus.Intact, control.SealStatus);
        Assert.Empty(await verifyContext.Set<ContainerControlEvent>()
            .Where(entry => entry.Action == ContainerControlAction.SealBroken)
            .ToListAsync());
    }

    private static BreakContainerSealService CreateService(DbContext dbContext)
    {
        return new BreakContainerSealService(dbContext, new FixedTimeProvider(FixedUtcNow));
    }

    private static AssignContainerSealService CreateSealService(DbContext dbContext)
    {
        return new AssignContainerSealService(dbContext, new FixedTimeProvider(FixedUtcNow));
    }

    private static async Task<Container> SeedContainerAsync(
        HarborOpsDbContext dbContext,
        string containerNumber = "MSKU1234567")
    {
        var container = Container.Register(containerNumber, "40HC", CargoStatus.Registered);

        await dbContext.Set<Container>().AddAsync(container, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return container;
    }

    private static async Task<Container> SeedSealedContainerAsync(HarborOpsDbContext dbContext)
    {
        var container = await SeedContainerAsync(dbContext);
        var control = ContainerControl.Start(container.Id);
        var sealEntry = control.AssignSeal("ML458821", ActorUserId, FixedUtcNow.UtcDateTime);

        await dbContext.Set<ContainerControl>().AddAsync(control, CancellationToken.None);
        await dbContext.Set<ContainerControlEvent>().AddAsync(sealEntry, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return container;
    }

    private static async Task<Container> SeedReleasedAsync(HarborOpsDbContext dbContext)
    {
        var container = await SeedContainerAsync(dbContext);
        var control = ContainerControl.Start(container.Id);
        var sealEntry = control.AssignSeal("ML458821", ActorUserId, FixedUtcNow.UtcDateTime);
        var inspectionEntries = control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Doors aligned",
            null,
            isReefer: false,
            ActorUserId,
            FixedUtcNow.UtcDateTime);
        var releaseEntry = control.Release(ActorUserId, FixedUtcNow.UtcDateTime, null);

        await dbContext.Set<ContainerControl>().AddAsync(control, CancellationToken.None);
        await dbContext.Set<ContainerControlEvent>()
            .AddRangeAsync([sealEntry, .. inspectionEntries, releaseEntry], CancellationToken.None);
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
