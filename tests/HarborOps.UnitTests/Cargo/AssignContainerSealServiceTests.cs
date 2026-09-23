using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class AssignContainerSealServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);

    private static readonly Guid ActorUserId =
        Guid.Parse("4d3c2b1a-0f9e-4d8c-9b7a-6f5e4d3c2b1a");

    [Fact]
    public async Task AssignAsync_normalizes_the_seal_number_and_starts_the_control()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedContainerAsync(dbContext);

        var details = await CreateService(dbContext).AssignAsync(
            container.Id,
            ActorUserId,
            new AssignContainerSealRequest { SealNumber = "  ml 45 8821 " },
            CancellationToken.None);

        Assert.Equal("ML458821", details.SealNumber);
        Assert.Equal(nameof(ContainerSealStatus.Intact), details.SealStatus);
        Assert.Equal(nameof(ContainerControlState.Pending), details.State);
        Assert.False(details.CanRelease);
    }

    [Fact]
    public async Task AssignAsync_saves_the_control_and_the_history_entry_together()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedContainerAsync(dbContext);

        await CreateService(dbContext).AssignAsync(
            container.Id,
            ActorUserId,
            new AssignContainerSealRequest { SealNumber = "ML458821" },
            CancellationToken.None);

        var control = await dbContext.Set<ContainerControl>()
            .SingleAsync(candidate => candidate.ContainerId == container.Id);
        var entry = await dbContext.Set<ContainerControlEvent>()
            .SingleAsync(candidate => candidate.ContainerControlId == control.Id);

        Assert.Equal(ContainerControlAction.SealAssigned, entry.Action);
        Assert.Equal("ML458821", entry.SealNumber);
        Assert.Equal(ActorUserId, entry.ActorUserId);
        Assert.Equal(FixedUtcNow.UtcDateTime, entry.OccurredUtc);
    }

    [Fact]
    public async Task AssignAsync_rejects_a_seal_that_is_active_on_another_container()
    {
        await using var dbContext = CreateDbContext();
        var first = await SeedContainerAsync(dbContext, "MSKU1111111");
        var second = await SeedContainerAsync(dbContext, "MSKU2222222");
        var service = CreateService(dbContext);

        await service.AssignAsync(
            first.Id,
            ActorUserId,
            new AssignContainerSealRequest { SealNumber = "ML458821" },
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateActiveSealException>(
            () => service.AssignAsync(
                second.Id,
                ActorUserId,
                new AssignContainerSealRequest { SealNumber = "ml458821" },
                CancellationToken.None));

        Assert.Equal("ML458821", exception.SealNumber);
        Assert.Equal(1, await dbContext.Set<ContainerControl>().CountAsync());
    }

    [Fact]
    public async Task AssignAsync_allows_a_seal_that_is_only_broken_elsewhere()
    {
        await using var dbContext = CreateDbContext();
        var first = await SeedContainerAsync(dbContext, "MSKU1111111");
        var second = await SeedContainerAsync(dbContext, "MSKU2222222");
        var service = CreateService(dbContext);

        await service.AssignAsync(
            first.Id,
            ActorUserId,
            new AssignContainerSealRequest { SealNumber = "ML458821" },
            CancellationToken.None);

        var firstControl = await dbContext.Set<ContainerControl>()
            .SingleAsync(candidate => candidate.ContainerId == first.Id);
        firstControl.BreakSeal(ActorUserId, FixedUtcNow.UtcDateTime, null);
        await dbContext.SaveChangesAsync();

        var details = await service.AssignAsync(
            second.Id,
            ActorUserId,
            new AssignContainerSealRequest { SealNumber = "ML458821" },
            CancellationToken.None);

        Assert.Equal("ML458821", details.SealNumber);
    }

    [Fact]
    public async Task AssignAsync_rejects_a_seal_the_container_already_carries()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedContainerAsync(dbContext);
        var service = CreateService(dbContext);

        await service.AssignAsync(
            container.Id,
            ActorUserId,
            new AssignContainerSealRequest { SealNumber = "ML458821" },
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ContainerControlTransitionException>(
            () => service.AssignAsync(
                container.Id,
                ActorUserId,
                new AssignContainerSealRequest { SealNumber = "  ml 458 821 " },
                CancellationToken.None));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.SealAlreadyAssignedReason,
            exception.Reason);
        Assert.Equal(1, await dbContext.Set<ContainerControlEvent>().CountAsync());
    }

    [Fact]
    public async Task AssignAsync_revokes_a_release_when_the_seal_is_replaced()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedContainerAsync(dbContext);
        var service = CreateService(dbContext);

        await service.AssignAsync(
            container.Id,
            ActorUserId,
            new AssignContainerSealRequest { SealNumber = "ML458821" },
            CancellationToken.None);

        var control = await dbContext.Set<ContainerControl>()
            .SingleAsync(candidate => candidate.ContainerId == container.Id);
        control.RecordInspection(
            ContainerInspectionResult.Passed,
            null,
            null,
            isReefer: false,
            ActorUserId,
            FixedUtcNow.UtcDateTime);
        control.Release(ActorUserId, FixedUtcNow.UtcDateTime, null, false);
        await dbContext.SaveChangesAsync();

        var details = await service.AssignAsync(
            container.Id,
            ActorUserId,
            new AssignContainerSealRequest { SealNumber = "ML999000" },
            CancellationToken.None);

        Assert.Equal("ML999000", details.SealNumber);
        Assert.Equal(nameof(ContainerControlState.Pending), details.State);
        Assert.True(details.CanRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ML/458821")]
    [InlineData("MLMLMLMLMLMLMLMLMLMLMLMLMLMLMLML")]
    public async Task AssignAsync_rejects_an_invalid_seal_number(string? sealNumber)
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedContainerAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ContainerControlValidationException>(
            () => CreateService(dbContext).AssignAsync(
                container.Id,
                ActorUserId,
                new AssignContainerSealRequest { SealNumber = sealNumber },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ContainerControlValidator.SealNumberField));
        Assert.Empty(await dbContext.Set<ContainerControl>().ToListAsync());
    }

    [Fact]
    public async Task AssignAsync_rejects_an_unknown_container()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<ContainerNotFoundException>(
            () => CreateService(dbContext).AssignAsync(
                Guid.NewGuid(),
                ActorUserId,
                new AssignContainerSealRequest { SealNumber = "ML458821" },
                CancellationToken.None));
    }

    [Fact]
    public async Task AssignAsync_writes_nothing_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid containerId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            containerId = (await SeedContainerAsync(seedContext)).Id;
        }

        await using (var failingContext = CreateDbContext(databaseName, failOnSave: true))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => CreateService(failingContext).AssignAsync(
                    containerId,
                    ActorUserId,
                    new AssignContainerSealRequest { SealNumber = "ML458821" },
                    CancellationToken.None));
        }

        await using var verifyContext = CreateDbContext(databaseName);

        Assert.Empty(await verifyContext.Set<ContainerControl>().ToListAsync());
        Assert.Empty(await verifyContext.Set<ContainerControlEvent>().ToListAsync());
    }

    private static AssignContainerSealService CreateService(DbContext dbContext)
    {
        return new AssignContainerSealService(dbContext, new FixedTimeProvider(FixedUtcNow));
    }

    private static async Task<Container> SeedContainerAsync(
        HarborOpsDbContext dbContext,
        string containerNumber = "MSKU1234567",
        string containerType = "40HC")
    {
        var container = Container.Register(containerNumber, containerType, CargoStatus.Registered);

        await dbContext.Set<Container>().AddAsync(container, CancellationToken.None);
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
