using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetDryDockDetailsQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 6, 15);

    [Fact]
    public async Task GetDetailsAsync_returns_every_field_of_a_planned_dry_dock()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var portId = Guid.NewGuid();
        var dryDock = DryDock.Schedule(vesselId, portId, Today.AddDays(4), "Rudder survey");
        await SeedAsync(dbContext, dryDock);

        var details = await CreateQuery(dbContext).GetDetailsAsync(dryDock.Id, CancellationToken.None);

        Assert.Equal(dryDock.Id, details.DryDockId);
        Assert.Equal(vesselId, details.VesselId);
        Assert.Equal(portId, details.PortId);
        Assert.Equal(Today.AddDays(4), details.StartDate);
        Assert.Null(details.EndDate);
        Assert.Equal("Rudder survey", details.PlannedTasks);
        Assert.Null(details.ActualCost);
        Assert.Equal("Planned", details.Status);
        Assert.False(details.IsOverdue);
        Assert.Equal(4, details.DaysUntilStart);
    }

    [Fact]
    public async Task A_planned_dry_dock_offers_starting_and_cancelling()
    {
        await using var dbContext = CreateDbContext();
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Planned work");
        await SeedAsync(dbContext, dryDock);

        var details = await CreateQuery(dbContext).GetDetailsAsync(dryDock.Id, CancellationToken.None);

        Assert.Equal(["InProgress", "Canceled"], details.AllowedNextStatuses);
    }

    [Fact]
    public async Task A_dry_dock_in_progress_offers_only_completion()
    {
        await using var dbContext = CreateDbContext();
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Running work");
        dryDock.Start();
        await SeedAsync(dbContext, dryDock);

        var details = await CreateQuery(dbContext).GetDetailsAsync(dryDock.Id, CancellationToken.None);

        Assert.Equal("InProgress", details.Status);
        Assert.Equal(["Completed"], details.AllowedNextStatuses);
    }

    [Fact]
    public async Task A_completed_dry_dock_reports_its_end_date_cost_and_no_further_action()
    {
        await using var dbContext = CreateDbContext();
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(-10), "Finished work");
        dryDock.Start();
        dryDock.Complete(Today.AddDays(-2), 84_500.75m);
        await SeedAsync(dbContext, dryDock);

        var details = await CreateQuery(dbContext).GetDetailsAsync(dryDock.Id, CancellationToken.None);

        Assert.Equal("Completed", details.Status);
        Assert.Equal(Today.AddDays(-2), details.EndDate);
        Assert.Equal(84_500.75m, details.ActualCost);
        Assert.Empty(details.AllowedNextStatuses);
    }

    [Fact]
    public async Task A_cancelled_dry_dock_keeps_its_planned_record_and_offers_no_action()
    {
        await using var dbContext = CreateDbContext();
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(6), "Abandoned work");
        dryDock.Cancel();
        await SeedAsync(dbContext, dryDock);

        var details = await CreateQuery(dbContext).GetDetailsAsync(dryDock.Id, CancellationToken.None);

        Assert.Equal("Canceled", details.Status);
        Assert.Equal(Today.AddDays(6), details.StartDate);
        Assert.Equal("Abandoned work", details.PlannedTasks);
        Assert.Null(details.EndDate);
        Assert.Null(details.ActualCost);
        Assert.Empty(details.AllowedNextStatuses);
    }

    [Fact]
    public async Task An_overdue_dry_dock_reports_the_days_it_is_late()
    {
        await using var dbContext = CreateDbContext();
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(-7), "Late work");
        await SeedAsync(dbContext, dryDock);

        var details = await CreateQuery(dbContext).GetDetailsAsync(dryDock.Id, CancellationToken.None);

        Assert.True(details.IsOverdue);
        Assert.Equal(-7, details.DaysUntilStart);
    }

    [Fact]
    public async Task GetDetailsAsync_ignores_other_dry_docks()
    {
        await using var dbContext = CreateDbContext();
        var wanted = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Wanted");
        await SeedAsync(
            dbContext,
            wanted,
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Other"));

        var details = await CreateQuery(dbContext).GetDetailsAsync(wanted.Id, CancellationToken.None);

        Assert.Equal("Wanted", details.PlannedTasks);
    }

    [Fact]
    public async Task GetDetailsAsync_rejects_an_unknown_dry_dock()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<DryDockNotFoundException>(
            () => CreateQuery(dbContext).GetDetailsAsync(missingId, CancellationToken.None));

        Assert.Equal(missingId, exception.DryDockId);
    }

    private static async Task SeedAsync(HarborOpsDbContext dbContext, params DryDock[] dryDocks)
    {
        await dbContext.Set<DryDock>().AddRangeAsync(dryDocks);
        await dbContext.SaveChangesAsync();
    }

    private static GetDryDockDetailsQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetDryDockDetailsQuery(dbContext, new FixedTimeProvider(FixedNow));
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
