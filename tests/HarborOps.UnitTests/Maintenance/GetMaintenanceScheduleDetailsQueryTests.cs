using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetMaintenanceScheduleDetailsQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 18);

    [Fact]
    public async Task GetDetailsAsync_returns_the_schedule_with_its_equipment_ordered_by_name()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var winch = await SeedEquipmentAsync(dbContext, vesselId, "Winch", "Deck", "MacGregor", "SN-3003");
        var boiler = await SeedEquipmentAsync(dbContext, vesselId, "Boiler", "Auxiliary", null, null);

        var scheduleId = await SeedScheduleAsync(
            dbContext,
            "Annual survey",
            MaintenanceFrequency.Annual,
            Today.AddDays(-5),
            400,
            winch.Id,
            boiler.Id);

        var response = await CreateQuery(dbContext).GetDetailsAsync(scheduleId, CancellationToken.None);

        Assert.Equal(scheduleId, response.ScheduleId);
        Assert.Equal(vesselId, response.VesselId);
        Assert.Equal("Annual survey", response.MaintenanceType);
        Assert.Equal("Annual", response.Frequency);
        Assert.Equal(400, response.NextDueHours);
        Assert.True(response.IsOverdue);
        Assert.Equal(-5, response.DaysUntilDue);
        Assert.Equal(["Boiler", "Winch"], response.Equipment.Select(item => item.Name));
        Assert.Equal("MacGregor", response.Equipment[1].Manufacturer);
        Assert.Equal("SN-3003", response.Equipment[1].SerialNumber);
    }

    [Fact]
    public async Task GetDetailsAsync_throws_when_the_schedule_does_not_exist()
    {
        await using var dbContext = CreateDbContext();
        var scheduleId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<MaintenanceScheduleNotFoundException>(
            () => CreateQuery(dbContext).GetDetailsAsync(scheduleId, CancellationToken.None));

        Assert.Equal(scheduleId, exception.ScheduleId);
    }

    private static GetMaintenanceScheduleDetailsQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetMaintenanceScheduleDetailsQuery(dbContext, new FixedTimeProvider(FixedNow));
    }

    private static async Task<Equipment> SeedEquipmentAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        string name,
        string category,
        string? manufacturer,
        string? serialNumber)
    {
        var equipment = Equipment.Register(vesselId, name, category, manufacturer, serialNumber);

        await dbContext.Set<Equipment>().AddAsync(equipment);
        await dbContext.SaveChangesAsync();

        return equipment;
    }

    private static async Task<Guid> SeedScheduleAsync(
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

        return schedule.Id;
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
