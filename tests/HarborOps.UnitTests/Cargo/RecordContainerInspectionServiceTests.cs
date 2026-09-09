using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class RecordContainerInspectionServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 7, 9, 15, 0, TimeSpan.Zero);

    private static readonly Guid ActorUserId =
        Guid.Parse("5e4d3c2b-1a0f-4e9d-8c7b-6a5f4e3d2c1b");

    [Theory]
    [InlineData(nameof(ContainerInspectionResult.Passed))]
    [InlineData(nameof(ContainerInspectionResult.Failed))]
    public async Task RecordAsync_stores_the_outcome_with_the_server_timestamp(string result)
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedSealedContainerAsync(dbContext);

        var details = await CreateService(dbContext).RecordAsync(
            container.Id,
            ActorUserId,
            new RecordContainerInspectionRequest
            {
                Result = result,
                ConditionNotes = "  Doors aligned  ",
            },
            CancellationToken.None);

        Assert.Equal(result, details.LatestInspectionResult);
        Assert.Equal("Doors aligned", details.LatestInspectionNotes);
        Assert.Equal(FixedUtcNow.UtcDateTime, details.LatestInspectedAtUtc);
        Assert.Equal(ActorUserId, details.LatestInspectedByUserId);
    }

    [Fact]
    public async Task RecordAsync_appends_a_history_entry()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedSealedContainerAsync(dbContext);

        await CreateService(dbContext).RecordAsync(
            container.Id,
            ActorUserId,
            new RecordContainerInspectionRequest
            {
                Result = nameof(ContainerInspectionResult.Passed),
                ConditionNotes = "Doors aligned",
            },
            CancellationToken.None);

        var entries = await dbContext.Set<ContainerControlEvent>()
            .OrderBy(entry => entry.OccurredUtc)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal(ContainerControlAction.InspectionRecorded, entries[^1].Action);
        Assert.Equal(ContainerInspectionResult.Passed, entries[^1].InspectionResult);
    }

    [Fact]
    public async Task RecordAsync_saves_the_lifted_hold_as_its_own_history_entry()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedSealedContainerAsync(dbContext);

        var control = await dbContext.Set<ContainerControl>()
            .SingleAsync(candidate => candidate.ContainerId == container.Id);
        control.Hold("Damaged corner post", ActorUserId, FixedUtcNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        var details = await CreateService(dbContext).RecordAsync(
            container.Id,
            ActorUserId,
            new RecordContainerInspectionRequest
            {
                Result = nameof(ContainerInspectionResult.Passed),
                ConditionNotes = "Corner post repaired",
            },
            CancellationToken.None);

        var lifted = await dbContext.Set<ContainerControlEvent>()
            .SingleAsync(entry => entry.Action == ContainerControlAction.HoldLifted);

        Assert.Equal(nameof(ContainerControlState.Pending), details.State);
        Assert.Equal(ContainerControlState.Held, lifted.FromState);
        Assert.Equal(ContainerControlState.Pending, lifted.ToState);
        Assert.Equal("Damaged corner post", lifted.Notes);
        Assert.Equal(ActorUserId, lifted.ActorUserId);
    }

    [Fact]
    public async Task RecordAsync_accepts_a_temperature_for_a_reefer_container()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedSealedContainerAsync(dbContext, containerType: "40RH");

        var details = await CreateService(dbContext).RecordAsync(
            container.Id,
            ActorUserId,
            new RecordContainerInspectionRequest
            {
                Result = nameof(ContainerInspectionResult.Passed),
                ConditionNotes = "Cooling steady",
                TemperatureCelsius = -18.5m,
            },
            CancellationToken.None);

        Assert.True(details.IsReefer);
        Assert.Equal(-18.5m, details.LatestInspectionTemperatureCelsius);
    }

    [Fact]
    public async Task RecordAsync_rejects_a_temperature_for_a_dry_container()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedSealedContainerAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ContainerControlValidationException>(
            () => CreateService(dbContext).RecordAsync(
                container.Id,
                ActorUserId,
                new RecordContainerInspectionRequest
                {
                    Result = nameof(ContainerInspectionResult.Passed),
                    TemperatureCelsius = -18.5m,
                },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ContainerControlValidator.TemperatureCelsiusField));

        var control = await dbContext.Set<ContainerControl>().SingleAsync();
        Assert.Null(control.LatestInspectionResult);
        Assert.Equal(1, await dbContext.Set<ContainerControlEvent>().CountAsync());
    }

    [Theory]
    [InlineData(-60.01)]
    [InlineData(60.01)]
    public async Task RecordAsync_rejects_a_temperature_outside_the_supported_range(
        double temperature)
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedSealedContainerAsync(dbContext, containerType: "40RH");

        var exception = await Assert.ThrowsAsync<ContainerControlValidationException>(
            () => CreateService(dbContext).RecordAsync(
                container.Id,
                ActorUserId,
                new RecordContainerInspectionRequest
                {
                    Result = nameof(ContainerInspectionResult.Passed),
                    TemperatureCelsius = (decimal)temperature,
                },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ContainerControlValidator.TemperatureCelsiusField));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown")]
    public async Task RecordAsync_rejects_an_invalid_result(string? result)
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedSealedContainerAsync(dbContext);

        var exception = await Assert.ThrowsAsync<ContainerControlValidationException>(
            () => CreateService(dbContext).RecordAsync(
                container.Id,
                ActorUserId,
                new RecordContainerInspectionRequest { Result = result },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(ContainerControlValidator.ResultField));
    }

    [Fact]
    public async Task RecordAsync_revokes_a_release_when_the_container_fails()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedReleasedContainerAsync(dbContext);

        var details = await CreateService(dbContext).RecordAsync(
            container.Id,
            ActorUserId,
            new RecordContainerInspectionRequest
            {
                Result = nameof(ContainerInspectionResult.Failed),
                ConditionNotes = "Door seal damaged",
            },
            CancellationToken.None);

        Assert.Equal(nameof(ContainerControlState.Pending), details.State);
        Assert.False(details.CanRelease);
    }

    [Fact]
    public async Task RecordAsync_starts_a_control_for_a_container_without_one()
    {
        await using var dbContext = CreateDbContext();
        var container = await SeedContainerAsync(dbContext);

        var details = await CreateService(dbContext).RecordAsync(
            container.Id,
            ActorUserId,
            new RecordContainerInspectionRequest
            {
                Result = nameof(ContainerInspectionResult.Passed),
            },
            CancellationToken.None);

        Assert.Equal(nameof(ContainerSealStatus.None), details.SealStatus);
        Assert.Equal(1, await dbContext.Set<ContainerControl>().CountAsync());
    }

    [Fact]
    public async Task RecordAsync_rejects_an_unknown_container()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<ContainerNotFoundException>(
            () => CreateService(dbContext).RecordAsync(
                Guid.NewGuid(),
                ActorUserId,
                new RecordContainerInspectionRequest
                {
                    Result = nameof(ContainerInspectionResult.Passed),
                },
                CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_writes_nothing_when_the_save_fails()
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
                () => CreateService(failingContext).RecordAsync(
                    containerId,
                    ActorUserId,
                    new RecordContainerInspectionRequest
                    {
                        Result = nameof(ContainerInspectionResult.Passed),
                    },
                    CancellationToken.None));
        }

        await using var verifyContext = CreateDbContext(databaseName);

        Assert.Empty(await verifyContext.Set<ContainerControl>().ToListAsync());
        Assert.Empty(await verifyContext.Set<ContainerControlEvent>().ToListAsync());
    }

    private static RecordContainerInspectionService CreateService(DbContext dbContext)
    {
        return new RecordContainerInspectionService(dbContext, new FixedTimeProvider(FixedUtcNow));
    }

    private static async Task<Container> SeedContainerAsync(
        HarborOpsDbContext dbContext,
        string containerType = "40HC")
    {
        var container = Container.Register("MSKU1234567", containerType, CargoStatus.Registered);

        await dbContext.Set<Container>().AddAsync(container, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return container;
    }

    private static async Task<Container> SeedSealedContainerAsync(
        HarborOpsDbContext dbContext,
        string containerType = "40HC")
    {
        var container = await SeedContainerAsync(dbContext, containerType);
        var control = ContainerControl.Start(container.Id);
        var entry = control.AssignSeal("ML458821", ActorUserId, FixedUtcNow.UtcDateTime);

        await dbContext.Set<ContainerControl>().AddAsync(control, CancellationToken.None);
        await dbContext.Set<ContainerControlEvent>().AddAsync(entry, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return container;
    }

    private static async Task<Container> SeedReleasedContainerAsync(HarborOpsDbContext dbContext)
    {
        var container = await SeedContainerAsync(dbContext);
        var control = ContainerControl.Start(container.Id);
        var sealEntry = control.AssignSeal("ML458821", ActorUserId, FixedUtcNow.UtcDateTime);
        var inspectionEntry = control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Sound",
            null,
            isReefer: false,
            ActorUserId,
            FixedUtcNow.UtcDateTime);
        var releaseEntry = control.Release(ActorUserId, FixedUtcNow.UtcDateTime, null);

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
