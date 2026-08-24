using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetDryDocksQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 6, 15);

    [Fact]
    public async Task GetDryDocksAsync_returns_every_stored_field()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var portId = Guid.NewGuid();
        var dryDock = DryDock.Schedule(vesselId, portId, Today.AddDays(10), "Hull coating");
        await SeedAsync(dbContext, dryDock);

        var response = await CreateQuery(dbContext).GetDryDocksAsync(
            new GetDryDocksRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(dryDock.Id, item.DryDockId);
        Assert.Equal(vesselId, item.VesselId);
        Assert.Equal(portId, item.PortId);
        Assert.Equal(Today.AddDays(10), item.StartDate);
        Assert.Null(item.EndDate);
        Assert.Equal("Hull coating", item.PlannedTasks);
        Assert.Null(item.ActualCost);
        Assert.Equal("Planned", item.Status);
    }

    [Fact]
    public async Task GetDryDocksAsync_filters_by_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        await SeedAsync(
            dbContext,
            DryDock.Schedule(vesselId, Guid.NewGuid(), Today, "Wanted"),
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Other vessel"));

        var response = await CreateQuery(dbContext).GetDryDocksAsync(
            new GetDryDocksRequest { VesselId = vesselId },
            CancellationToken.None);

        Assert.Equal("Wanted", Assert.Single(response.Items).PlannedTasks);
    }

    [Fact]
    public async Task GetDryDocksAsync_filters_by_port()
    {
        await using var dbContext = CreateDbContext();
        var portId = Guid.NewGuid();
        await SeedAsync(
            dbContext,
            DryDock.Schedule(Guid.NewGuid(), portId, Today, "Wanted"),
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Other port"));

        var response = await CreateQuery(dbContext).GetDryDocksAsync(
            new GetDryDocksRequest { PortId = portId },
            CancellationToken.None);

        Assert.Equal("Wanted", Assert.Single(response.Items).PlannedTasks);
    }

    [Fact]
    public async Task GetDryDocksAsync_filters_by_status()
    {
        await using var dbContext = CreateDbContext();
        var started = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Started");
        started.Start();
        await SeedAsync(
            dbContext,
            started,
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Still planned"));

        var response = await CreateQuery(dbContext).GetDryDocksAsync(
            new GetDryDocksRequest { Status = "inprogress" },
            CancellationToken.None);

        Assert.Equal("Started", Assert.Single(response.Items).PlannedTasks);
    }

    [Fact]
    public async Task GetDryDocksAsync_filters_by_start_date_range()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(
            dbContext,
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(-5), "Too early"),
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(5), "Inside"),
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(50), "Too late"));

        var response = await CreateQuery(dbContext).GetDryDocksAsync(
            new GetDryDocksRequest { StartFrom = Today, StartTo = Today.AddDays(30) },
            CancellationToken.None);

        Assert.Equal("Inside", Assert.Single(response.Items).PlannedTasks);
    }

    [Fact]
    public async Task GetDryDocksAsync_returns_only_overdue_dry_docks_when_asked()
    {
        await using var dbContext = CreateDbContext();
        var completedInThePast = DryDock.Schedule(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Today.AddDays(-9),
            "Done");
        completedInThePast.Start();
        completedInThePast.Complete(Today.AddDays(-1), 10m);
        await SeedAsync(
            dbContext,
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(-3), "Overdue"),
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(3), "Upcoming"),
            completedInThePast);

        var response = await CreateQuery(dbContext).GetDryDocksAsync(
            new GetDryDocksRequest { Overdue = true },
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal("Overdue", item.PlannedTasks);
        Assert.True(item.IsOverdue);
        Assert.Equal(-3, item.DaysUntilStart);
    }

    [Fact]
    public async Task GetDryDocksAsync_excludes_overdue_dry_docks_when_asked_for_upcoming()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(
            dbContext,
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(-3), "Overdue"),
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Starting today"));

        var response = await CreateQuery(dbContext).GetDryDocksAsync(
            new GetDryDocksRequest { Overdue = false },
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal("Starting today", item.PlannedTasks);
        Assert.False(item.IsOverdue);
        Assert.Equal(0, item.DaysUntilStart);
    }

    [Fact]
    public async Task A_dry_dock_that_is_no_longer_planned_is_never_overdue()
    {
        await using var dbContext = CreateDbContext();
        var started = DryDock.Schedule(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Today.AddDays(-30),
            "Running late");
        started.Start();
        await SeedAsync(dbContext, started);

        var response = await CreateQuery(dbContext).GetDryDocksAsync(
            new GetDryDocksRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.False(item.IsOverdue);
        Assert.Null(item.DaysUntilStart);
    }

    [Fact]
    public async Task GetDryDocksAsync_searches_the_planned_tasks()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(
            dbContext,
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Propeller shaft survey"),
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Hull coating"));

        var response = await CreateQuery(dbContext).GetDryDocksAsync(
            new GetDryDocksRequest { Search = " shaft " },
            CancellationToken.None);

        Assert.Equal("Propeller shaft survey", Assert.Single(response.Items).PlannedTasks);
    }

    [Fact]
    public async Task GetDryDocksAsync_orders_by_start_date_then_identifier()
    {
        await using var dbContext = CreateDbContext();
        var sameDay = Today.AddDays(2);
        var first = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), sameDay, "Same day A");
        var second = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), sameDay, "Same day B");
        await SeedAsync(
            dbContext,
            second,
            first,
            DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Earliest"));

        var response = await CreateQuery(dbContext).GetDryDocksAsync(
            new GetDryDocksRequest(),
            CancellationToken.None);

        var expected = new[] { first.Id, second.Id }.OrderBy(id => id).ToArray();

        Assert.Equal("Earliest", response.Items[0].PlannedTasks);
        Assert.Equal(expected[0], response.Items[1].DryDockId);
        Assert.Equal(expected[1], response.Items[2].DryDockId);
    }

    [Fact]
    public async Task GetDryDocksAsync_pages_without_losing_the_total_count()
    {
        await using var dbContext = CreateDbContext();
        var dryDocks = Enumerable.Range(0, 5)
            .Select(offset => DryDock.Schedule(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Today.AddDays(offset),
                $"Task {offset}"))
            .ToArray();
        await SeedAsync(dbContext, dryDocks);

        var response = await CreateQuery(dbContext).GetDryDocksAsync(
            new GetDryDocksRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(["Task 2", "Task 3"], response.Items.Select(item => item.PlannedTasks));
    }

    [Fact]
    public async Task GetDryDocksAsync_applies_the_default_page_when_none_is_asked_for()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), Today, "Only"));

        var response = await CreateQuery(dbContext).GetDryDocksAsync(
            new GetDryDocksRequest(),
            CancellationToken.None);

        Assert.Equal(GetDryDocksValidator.DefaultPageNumber, response.PageNumber);
        Assert.Equal(GetDryDocksValidator.DefaultPageSize, response.PageSize);
    }

    [Fact]
    public async Task GetDryDocksAsync_rejects_an_unknown_status()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetDryDocksValidationException>(
            () => CreateQuery(dbContext).GetDryDocksAsync(
                new GetDryDocksRequest { Status = "Sailing" },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(GetDryDocksValidator.StatusField));
    }

    [Fact]
    public async Task GetDryDocksAsync_rejects_a_page_size_beyond_the_bound()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetDryDocksValidationException>(
            () => CreateQuery(dbContext).GetDryDocksAsync(
                new GetDryDocksRequest { PageSize = GetDryDocksValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(GetDryDocksValidator.PageSizeField));
    }

    [Fact]
    public async Task GetDryDocksAsync_rejects_a_reversed_date_range()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetDryDocksValidationException>(
            () => CreateQuery(dbContext).GetDryDocksAsync(
                new GetDryDocksRequest { StartFrom = Today, StartTo = Today.AddDays(-1) },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(GetDryDocksValidator.StartToField));
    }

    private static async Task SeedAsync(HarborOpsDbContext dbContext, params DryDock[] dryDocks)
    {
        await dbContext.Set<DryDock>().AddRangeAsync(dryDocks);
        await dbContext.SaveChangesAsync();
    }

    private static GetDryDocksQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetDryDocksQuery(dbContext, new FixedTimeProvider(FixedNow));
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
