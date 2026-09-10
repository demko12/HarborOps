using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetPreventiveWorkForecastQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 10);

    [Fact]
    public async Task GetForecastAsync_projects_a_due_occurrence_with_its_vessel_and_equipment_scope()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var mainEngine = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(dbContext, vesselId, "Boiler");
        var schedule = await SeedScheduleAsync(
            dbContext,
            "Main engine overhaul",
            MaintenanceFrequency.Monthly,
            Today.AddDays(5),
            null,
            mainEngine.Id,
            boiler.Id);

        var response = await CreateQuery(dbContext).GetForecastAsync(
            Window(Today, Today.AddDays(30)),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(schedule.Id, item.ScheduleId);
        Assert.Equal("Main engine overhaul", item.MaintenanceType);
        Assert.Equal("Monthly", item.Frequency);
        Assert.Equal(Today.AddDays(5), item.DueDate);
        Assert.Equal(new DateTime(2026, 9, 15, 23, 59, 59, DateTimeKind.Utc), item.DueUtc);
        Assert.Equal(vesselId, item.VesselId);
        Assert.Equal([boiler.Id, mainEngine.Id], item.EquipmentIds);
        Assert.Equal("Pending", item.State);
        Assert.Null(item.OccurrenceId);
        Assert.Null(item.WorkOrderId);
        Assert.False(item.IsOverdue);
    }

    [Fact]
    public async Task GetForecastAsync_projects_every_recurrence_inside_the_window()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Weekly check", MaintenanceFrequency.Weekly, Today, null, equipment.Id);

        var response = await CreateQuery(dbContext).GetForecastAsync(
            Window(Today, Today.AddDays(21)),
            CancellationToken.None);

        Assert.Equal(
            [Today, Today.AddDays(7), Today.AddDays(14), Today.AddDays(21)],
            response.Items.Select(item => item.DueDate));
    }

    [Fact]
    public async Task GetForecastAsync_skips_hour_only_and_undated_schedules()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");

        await SeedScheduleAsync(dbContext, "Dated", MaintenanceFrequency.Monthly, Today.AddDays(3), null, equipment.Id);
        await SeedScheduleAsync(dbContext, "Hours only", MaintenanceFrequency.RunningHours, null, 800, equipment.Id);
        await SeedScheduleAsync(
            dbContext,
            "Hours with a date",
            MaintenanceFrequency.RunningHours,
            Today.AddDays(3),
            800,
            equipment.Id);
        await SeedScheduleAsync(dbContext, "Undated", MaintenanceFrequency.Monthly, null, null, equipment.Id);

        var response = await CreateQuery(dbContext).GetForecastAsync(
            Window(Today, Today.AddDays(30)),
            CancellationToken.None);

        Assert.Equal("Dated", Assert.Single(response.Items).MaintenanceType);
    }

    [Fact]
    public async Task GetForecastAsync_skips_a_schedule_without_equipment_scope()
    {
        await using var dbContext = CreateDbContext();
        await SeedScheduleAsync(dbContext, "Unscoped", MaintenanceFrequency.Monthly, Today.AddDays(3), null);

        var response = await CreateQuery(dbContext).GetForecastAsync(
            Window(Today, Today.AddDays(30)),
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task GetForecastAsync_filters_by_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var mine = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var other = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Boiler");

        await SeedScheduleAsync(dbContext, "Mine", MaintenanceFrequency.Monthly, Today.AddDays(3), null, mine.Id);
        await SeedScheduleAsync(dbContext, "Other", MaintenanceFrequency.Monthly, Today.AddDays(3), null, other.Id);

        var response = await CreateQuery(dbContext).GetForecastAsync(
            Window(Today, Today.AddDays(30)) with { VesselId = vesselId },
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal("Mine", item.MaintenanceType);
        Assert.Equal(vesselId, item.VesselId);
    }

    [Fact]
    public async Task GetForecastAsync_links_a_generated_occurrence_to_its_work_order()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var equipment = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var schedule = await SeedScheduleAsync(
            dbContext,
            "Overhaul",
            MaintenanceFrequency.Monthly,
            Today.AddDays(5),
            null,
            equipment.Id);
        var (workOrder, occurrence) = await SeedGeneratedAsync(
            dbContext,
            schedule,
            vesselId,
            Today.AddDays(5),
            ScheduleOccurrenceState.Generated);

        var response = await CreateQuery(dbContext).GetForecastAsync(
            Window(Today, Today.AddDays(30)),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal("Generated", item.State);
        Assert.Equal(occurrence.Id, item.OccurrenceId);
        Assert.Equal(workOrder.Id, item.WorkOrderId);
    }

    [Fact]
    public async Task GetForecastAsync_keeps_a_resolved_occurrence_after_the_schedule_advanced()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var equipment = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var schedule = await SeedScheduleAsync(
            dbContext,
            "Overhaul",
            MaintenanceFrequency.Monthly,
            Today.AddDays(35),
            null,
            equipment.Id);
        var (workOrder, _) = await SeedGeneratedAsync(
            dbContext,
            schedule,
            vesselId,
            Today.AddDays(5),
            ScheduleOccurrenceState.Completed);

        var response = await CreateQuery(dbContext).GetForecastAsync(
            Window(Today, Today.AddDays(30)),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(Today.AddDays(5), item.DueDate);
        Assert.Equal("Completed", item.State);
        Assert.Equal(workOrder.Id, item.WorkOrderId);
    }

    [Fact]
    public async Task GetForecastAsync_filters_by_state_and_counts_the_whole_window()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var equipment = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var generated = await SeedScheduleAsync(
            dbContext,
            "Generated",
            MaintenanceFrequency.Monthly,
            Today.AddDays(2),
            null,
            equipment.Id);
        await SeedScheduleAsync(dbContext, "Pending", MaintenanceFrequency.Monthly, Today.AddDays(4), null, equipment.Id);
        await SeedGeneratedAsync(dbContext, generated, vesselId, Today.AddDays(2), ScheduleOccurrenceState.Generated);

        var response = await CreateQuery(dbContext).GetForecastAsync(
            Window(Today, Today.AddDays(10)) with { State = "generated" },
            CancellationToken.None);

        Assert.Equal("Generated", Assert.Single(response.Items).MaintenanceType);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(1, response.PendingCount);
        Assert.Equal(1, response.GeneratedCount);
    }

    [Fact]
    public async Task GetForecastAsync_reports_only_open_past_occurrences_as_overdue()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var equipment = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        await SeedScheduleAsync(dbContext, "Overdue", MaintenanceFrequency.Monthly, Today.AddDays(-3), null, equipment.Id);
        var completed = await SeedScheduleAsync(
            dbContext,
            "Completed",
            MaintenanceFrequency.Monthly,
            Today.AddDays(24),
            null,
            equipment.Id);
        await SeedGeneratedAsync(dbContext, completed, vesselId, Today.AddDays(-6), ScheduleOccurrenceState.Completed);

        var response = await CreateQuery(dbContext).GetForecastAsync(
            Window(Today.AddDays(-10), Today.AddDays(10)),
            CancellationToken.None);

        var overdue = Assert.Single(response.Items, item => item.MaintenanceType == "Overdue");
        var resolved = Assert.Single(response.Items, item => item.MaintenanceType == "Completed");

        Assert.True(overdue.IsOverdue);
        Assert.False(resolved.IsOverdue);
        Assert.Equal(1, response.OverdueCount);
    }

    [Fact]
    public async Task GetForecastAsync_keeps_an_overdue_due_date_that_falls_before_the_window()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Long overdue", MaintenanceFrequency.Annual, Today.AddDays(-40), null, equipment.Id);

        var response = await CreateQuery(dbContext).GetForecastAsync(
            Window(Today, Today.AddDays(10)),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(Today.AddDays(-40), item.DueDate);
        Assert.True(item.IsOverdue);
    }

    [Theory]
    [InlineData(14, 20, 10, false)]
    [InlineData(-12, 3, 9, true)]
    public async Task GetForecastAsync_classifies_overdue_by_the_utc_day_whatever_the_local_time_zone(
        int offsetHours,
        int utcHour,
        int dueDay,
        bool expectedOverdue)
    {
        await using var dbContext = CreateDbContext();
        var utcNow = new DateTimeOffset(2026, 9, 10, utcHour, 0, 0, TimeSpan.Zero);
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "HarborOpsTestZone",
            TimeSpan.FromHours(offsetHours),
            "HarborOps test zone",
            "HarborOps test zone");
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(
            dbContext,
            "Inspection",
            MaintenanceFrequency.Annual,
            new DateOnly(2026, 9, dueDay),
            null,
            equipment.Id);

        var query = new GetPreventiveWorkForecastQuery(dbContext, new ZonedTimeProvider(utcNow, zone));

        var response = await query.GetForecastAsync(
            Window(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)),
            CancellationToken.None);

        Assert.NotEqual(
            DateOnly.FromDateTime(utcNow.UtcDateTime),
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow.UtcDateTime, zone)));
        Assert.Equal(expectedOverdue, Assert.Single(response.Items).IsOverdue);
    }

    [Fact]
    public async Task GetForecastAsync_orders_by_due_date_then_schedule()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        var first = await SeedScheduleAsync(dbContext, "A", MaintenanceFrequency.Monthly, Today.AddDays(5), null, equipment.Id);
        var second = await SeedScheduleAsync(dbContext, "B", MaintenanceFrequency.Monthly, Today.AddDays(5), null, equipment.Id);
        var earliest = await SeedScheduleAsync(dbContext, "C", MaintenanceFrequency.Monthly, Today.AddDays(2), null, equipment.Id);

        var response = await CreateQuery(dbContext).GetForecastAsync(
            Window(Today, Today.AddDays(10)),
            CancellationToken.None);

        Assert.Equal(
            [earliest.Id, .. new[] { first.Id, second.Id }.Order()],
            response.Items.Select(item => item.ScheduleId));
    }

    [Fact]
    public async Task GetForecastAsync_pages_deterministically()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");

        for (var day = 1; day <= 5; day++)
        {
            await SeedScheduleAsync(
                dbContext,
                "Schedule",
                MaintenanceFrequency.Monthly,
                Today.AddDays(day),
                null,
                equipment.Id);
        }

        var query = CreateQuery(dbContext);
        var window = Window(Today, Today.AddDays(10));

        var all = await query.GetForecastAsync(window with { PageSize = 100 }, CancellationToken.None);
        var page = await query.GetForecastAsync(window with { PageNumber = 2, PageSize = 2 }, CancellationToken.None);
        var again = await query.GetForecastAsync(window with { PageNumber = 2, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(
            all.Items.Skip(2).Take(2).Select(item => item.ScheduleId),
            page.Items.Select(item => item.ScheduleId));
        Assert.Equal(
            page.Items.Select(item => item.ScheduleId),
            again.Items.Select(item => item.ScheduleId));
    }

    [Fact]
    public async Task GetForecastAsync_rejects_an_unbounded_window()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetPreventiveWorkForecastValidationException>(
            () => CreateQuery(dbContext).GetForecastAsync(
                new GetPreventiveWorkForecastRequest { From = Today },
                CancellationToken.None));

        Assert.Contains(GetPreventiveWorkForecastValidator.ToField, exception.Errors);
    }

    [Fact]
    public async Task GetForecastAsync_rejects_a_window_longer_than_the_limit()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetPreventiveWorkForecastValidationException>(
            () => CreateQuery(dbContext).GetForecastAsync(
                Window(Today, Today.AddDays(GetPreventiveWorkForecastValidator.MaxWindowDays + 1)),
                CancellationToken.None));

        Assert.Contains(GetPreventiveWorkForecastValidator.ToField, exception.Errors);
    }

    private static GetPreventiveWorkForecastRequest Window(DateOnly from, DateOnly to)
    {
        return new GetPreventiveWorkForecastRequest { From = from, To = to };
    }

    private static GetPreventiveWorkForecastQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetPreventiveWorkForecastQuery(dbContext, new FixedTimeProvider(FixedNow));
    }

    private static async Task<Equipment> SeedEquipmentAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        string name)
    {
        var equipment = Equipment.Register(vesselId, name, "Propulsion", null, null);

        await dbContext.Set<Equipment>().AddAsync(equipment);
        await dbContext.SaveChangesAsync();

        return equipment;
    }

    private static async Task<MaintenanceSchedule> SeedScheduleAsync(
        HarborOpsDbContext dbContext,
        string maintenanceType,
        MaintenanceFrequency frequency,
        DateOnly? nextDueDate,
        int? nextDueHours,
        params Guid[] equipmentIds)
    {
        var schedule = MaintenanceSchedule.Create(maintenanceType, frequency, nextDueDate, nextDueHours);

        await dbContext.Set<MaintenanceSchedule>().AddAsync(schedule);

        foreach (var equipmentId in equipmentIds)
        {
            await dbContext.Set<ScheduleEquipment>().AddAsync(ScheduleEquipment.Create(schedule.Id, equipmentId));
        }

        await dbContext.SaveChangesAsync();

        return schedule;
    }

    private static async Task<(MaintenanceWorkOrder WorkOrder, ScheduleOccurrence Occurrence)> SeedGeneratedAsync(
        HarborOpsDbContext dbContext,
        MaintenanceSchedule schedule,
        Guid vesselId,
        DateOnly dueDate,
        ScheduleOccurrenceState state)
    {
        var workOrder = MaintenanceWorkOrder.CreateFromSchedule(
            vesselId,
            "Preventive work",
            null,
            WorkOrderPriority.Medium,
            schedule.Id,
            dueDate);
        var occurrence = ScheduleOccurrence.Generate(schedule.Id, dueDate, workOrder.Id, FixedNow.UtcDateTime);

        switch (state)
        {
            case ScheduleOccurrenceState.Completed:
                workOrder.Complete(FixedNow.UtcDateTime);
                occurrence.MarkCompleted(workOrder.Id, FixedNow.UtcDateTime);
                break;

            case ScheduleOccurrenceState.Cancelled:
                workOrder.Cancel();
                occurrence.MarkCancelled(workOrder.Id, FixedNow.UtcDateTime);
                break;

            default:
                break;
        }

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<ScheduleOccurrence>().AddAsync(occurrence);
        await dbContext.SaveChangesAsync();

        return (workOrder, occurrence);
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

    private sealed class ZonedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localTimeZone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public override TimeZoneInfo LocalTimeZone => localTimeZone;
    }
}
