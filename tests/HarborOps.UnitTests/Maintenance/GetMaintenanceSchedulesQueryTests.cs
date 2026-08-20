using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetMaintenanceSchedulesQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 18);

    [Fact]
    public async Task GetSchedulesAsync_projects_the_vessel_and_the_equipment_count()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var mainEngine = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(dbContext, vesselId, "Boiler");

        await SeedScheduleAsync(
            dbContext,
            "Quarterly inspection",
            MaintenanceFrequency.Quarterly,
            Today.AddDays(10),
            null,
            mainEngine.Id,
            boiler.Id);

        var response = await CreateQuery(dbContext).GetSchedulesAsync(
            new GetMaintenanceSchedulesRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(vesselId, item.VesselId);
        Assert.Equal(2, item.EquipmentCount);
        Assert.Equal("Quarterly", item.Frequency);
        Assert.False(item.IsOverdue);
        Assert.Equal(10, item.DaysUntilDue);
    }

    [Fact]
    public async Task GetSchedulesAsync_classifies_due_state_from_the_captured_day()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var equipment = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");

        await SeedScheduleAsync(dbContext, "Overdue", MaintenanceFrequency.Monthly, Today.AddDays(-2), null, equipment.Id);
        await SeedScheduleAsync(dbContext, "Due today", MaintenanceFrequency.Monthly, Today, null, equipment.Id);
        await SeedScheduleAsync(dbContext, "Hours only", MaintenanceFrequency.RunningHours, null, 800, equipment.Id);

        var response = await CreateQuery(dbContext).GetSchedulesAsync(
            new GetMaintenanceSchedulesRequest(),
            CancellationToken.None);

        var overdue = response.Items.Single(item => item.MaintenanceType == "Overdue");
        var dueToday = response.Items.Single(item => item.MaintenanceType == "Due today");
        var hoursOnly = response.Items.Single(item => item.MaintenanceType == "Hours only");

        Assert.True(overdue.IsOverdue);
        Assert.Equal(-2, overdue.DaysUntilDue);
        Assert.False(dueToday.IsOverdue);
        Assert.Equal(0, dueToday.DaysUntilDue);
        Assert.False(hoursOnly.IsOverdue);
        Assert.Null(hoursOnly.DaysUntilDue);
        Assert.Equal(800, hoursOnly.NextDueHours);
    }

    [Fact]
    public async Task GetSchedulesAsync_orders_by_next_due_date_with_undated_schedules_last()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");

        await SeedScheduleAsync(dbContext, "Hours only", MaintenanceFrequency.RunningHours, null, 800, equipment.Id);
        await SeedScheduleAsync(dbContext, "Later", MaintenanceFrequency.Annual, Today.AddDays(30), null, equipment.Id);
        await SeedScheduleAsync(dbContext, "Sooner", MaintenanceFrequency.Monthly, Today.AddDays(3), null, equipment.Id);

        var response = await CreateQuery(dbContext).GetSchedulesAsync(
            new GetMaintenanceSchedulesRequest(),
            CancellationToken.None);

        Assert.Equal(
            ["Sooner", "Later", "Hours only"],
            response.Items.Select(item => item.MaintenanceType));
    }

    [Fact]
    public async Task GetSchedulesAsync_filters_by_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var mine = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var other = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Boiler");

        await SeedScheduleAsync(dbContext, "Mine", MaintenanceFrequency.Monthly, Today.AddDays(5), null, mine.Id);
        await SeedScheduleAsync(dbContext, "Other", MaintenanceFrequency.Monthly, Today.AddDays(5), null, other.Id);

        var response = await CreateQuery(dbContext).GetSchedulesAsync(
            new GetMaintenanceSchedulesRequest { VesselId = vesselId },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal("Mine", Assert.Single(response.Items).MaintenanceType);
    }

    [Fact]
    public async Task GetSchedulesAsync_filters_by_equipment()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var mainEngine = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(dbContext, vesselId, "Boiler");

        await SeedScheduleAsync(dbContext, "Engine", MaintenanceFrequency.Monthly, Today.AddDays(5), null, mainEngine.Id);
        await SeedScheduleAsync(dbContext, "Boiler", MaintenanceFrequency.Monthly, Today.AddDays(5), null, boiler.Id);

        var response = await CreateQuery(dbContext).GetSchedulesAsync(
            new GetMaintenanceSchedulesRequest { EquipmentId = boiler.Id },
            CancellationToken.None);

        Assert.Equal("Boiler", Assert.Single(response.Items).MaintenanceType);
    }

    [Fact]
    public async Task GetSchedulesAsync_filters_by_due_window()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");

        await SeedScheduleAsync(dbContext, "Before", MaintenanceFrequency.Monthly, Today.AddDays(1), null, equipment.Id);
        await SeedScheduleAsync(dbContext, "Inside", MaintenanceFrequency.Monthly, Today.AddDays(5), null, equipment.Id);
        await SeedScheduleAsync(dbContext, "After", MaintenanceFrequency.Monthly, Today.AddDays(20), null, equipment.Id);
        await SeedScheduleAsync(dbContext, "Hours only", MaintenanceFrequency.RunningHours, null, 800, equipment.Id);

        var response = await CreateQuery(dbContext).GetSchedulesAsync(
            new GetMaintenanceSchedulesRequest
            {
                DueFrom = Today.AddDays(3),
                DueTo = Today.AddDays(10),
            },
            CancellationToken.None);

        Assert.Equal("Inside", Assert.Single(response.Items).MaintenanceType);
    }

    [Fact]
    public async Task GetSchedulesAsync_filters_by_overdue_state()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");

        await SeedScheduleAsync(dbContext, "Overdue", MaintenanceFrequency.Monthly, Today.AddDays(-1), null, equipment.Id);
        await SeedScheduleAsync(dbContext, "Upcoming", MaintenanceFrequency.Monthly, Today.AddDays(1), null, equipment.Id);
        await SeedScheduleAsync(dbContext, "Hours only", MaintenanceFrequency.RunningHours, null, 800, equipment.Id);

        var overdue = await CreateQuery(dbContext).GetSchedulesAsync(
            new GetMaintenanceSchedulesRequest { Overdue = true },
            CancellationToken.None);

        var notOverdue = await CreateQuery(dbContext).GetSchedulesAsync(
            new GetMaintenanceSchedulesRequest { Overdue = false },
            CancellationToken.None);

        Assert.Equal("Overdue", Assert.Single(overdue.Items).MaintenanceType);
        Assert.Equal(["Upcoming", "Hours only"], notOverdue.Items.Select(item => item.MaintenanceType));
    }

    [Fact]
    public async Task GetSchedulesAsync_applies_custom_paging()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");

        for (var index = 1; index <= 5; index++)
        {
            await SeedScheduleAsync(
                dbContext,
                $"Schedule {index:D2}",
                MaintenanceFrequency.Monthly,
                Today.AddDays(index),
                null,
                equipment.Id);
        }

        var response = await CreateQuery(dbContext).GetSchedulesAsync(
            new GetMaintenanceSchedulesRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(["Schedule 03", "Schedule 04"], response.Items.Select(item => item.MaintenanceType));
    }

    [Fact]
    public async Task GetSchedulesAsync_rejects_an_inverted_due_window()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetMaintenanceSchedulesValidationException>(
            () => CreateQuery(dbContext).GetSchedulesAsync(
                new GetMaintenanceSchedulesRequest
                {
                    DueFrom = Today.AddDays(10),
                    DueTo = Today,
                },
                CancellationToken.None));

        Assert.Contains(GetMaintenanceSchedulesValidator.DueToField, exception.Errors);
    }

    [Fact]
    public async Task GetSchedulesAsync_rejects_an_invalid_page_number()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetMaintenanceSchedulesValidationException>(
            () => CreateQuery(dbContext).GetSchedulesAsync(
                new GetMaintenanceSchedulesRequest { PageNumber = 0 },
                CancellationToken.None));

        Assert.Contains(GetMaintenanceSchedulesValidator.PageNumberField, exception.Errors);
    }

    private static GetMaintenanceSchedulesQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetMaintenanceSchedulesQuery(dbContext, new FixedTimeProvider(FixedNow));
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

    private static async Task SeedScheduleAsync(
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
            await dbContext.Set<ScheduleEquipment>().AddAsync(
                ScheduleEquipment.Create(schedule.Id, equipmentId));
        }

        await dbContext.SaveChangesAsync();
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
