using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class RegisterMaintenanceScheduleServiceTests
{
    private static readonly DateOnly NextDueDate = new(2026, 9, 30);

    [Fact]
    public async Task RegisterAsync_links_every_requested_equipment_to_the_schedule()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var mainEngine = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(dbContext, vesselId, "Boiler");

        var service = new RegisterMaintenanceScheduleService(dbContext);

        var response = await service.RegisterAsync(
            new RegisterMaintenanceScheduleRequest
            {
                MaintenanceType = "  Quarterly inspection  ",
                Frequency = "quarterly",
                NextDueDate = NextDueDate,
                EquipmentIds = [mainEngine.Id, boiler.Id],
            },
            CancellationToken.None);

        var schedule = await dbContext.Set<MaintenanceSchedule>().SingleAsync();
        var links = await dbContext.Set<ScheduleEquipment>().ToListAsync();

        Assert.Equal(schedule.Id, response.Id);
        Assert.Equal("Quarterly inspection", schedule.MaintenanceType);
        Assert.Equal("Quarterly", schedule.Frequency);
        Assert.Equal(NextDueDate, schedule.NextDueDate);
        Assert.Null(schedule.NextDueHours);
        Assert.Equal(2, links.Count);
        Assert.All(links, link => Assert.Equal(schedule.Id, link.ScheduleId));
        Assert.Contains(links, link => link.EquipmentId == mainEngine.Id);
        Assert.Contains(links, link => link.EquipmentId == boiler.Id);
    }

    [Fact]
    public async Task RegisterAsync_accepts_a_running_hours_schedule_without_a_date()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var equipment = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");

        var service = new RegisterMaintenanceScheduleService(dbContext);

        await service.RegisterAsync(
            new RegisterMaintenanceScheduleRequest
            {
                MaintenanceType = "Bearing check",
                Frequency = "RunningHours",
                NextDueHours = 500,
                EquipmentIds = [equipment.Id],
            },
            CancellationToken.None);

        var schedule = await dbContext.Set<MaintenanceSchedule>().SingleAsync();

        Assert.Null(schedule.NextDueDate);
        Assert.Equal(500, schedule.NextDueHours);
    }

    [Fact]
    public async Task RegisterAsync_requires_at_least_one_due_trigger()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");

        var service = new RegisterMaintenanceScheduleService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterMaintenanceScheduleValidationException>(
            () => service.RegisterAsync(
                new RegisterMaintenanceScheduleRequest
                {
                    MaintenanceType = "Inspection",
                    Frequency = "Monthly",
                    EquipmentIds = [equipment.Id],
                },
                CancellationToken.None));

        Assert.Contains(RegisterMaintenanceScheduleValidator.NextDueDateField, exception.Errors);
    }

    [Fact]
    public async Task RegisterAsync_requires_hours_for_a_running_hours_schedule()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");

        var service = new RegisterMaintenanceScheduleService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterMaintenanceScheduleValidationException>(
            () => service.RegisterAsync(
                new RegisterMaintenanceScheduleRequest
                {
                    MaintenanceType = "Bearing check",
                    Frequency = "RunningHours",
                    NextDueDate = NextDueDate,
                    EquipmentIds = [equipment.Id],
                },
                CancellationToken.None));

        Assert.Contains(RegisterMaintenanceScheduleValidator.NextDueHoursField, exception.Errors);
    }

    [Fact]
    public async Task RegisterAsync_rejects_negative_running_hours()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");

        var service = new RegisterMaintenanceScheduleService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterMaintenanceScheduleValidationException>(
            () => service.RegisterAsync(
                new RegisterMaintenanceScheduleRequest
                {
                    MaintenanceType = "Bearing check",
                    Frequency = "Monthly",
                    NextDueHours = -1,
                    EquipmentIds = [equipment.Id],
                },
                CancellationToken.None));

        Assert.Contains(RegisterMaintenanceScheduleValidator.NextDueHoursField, exception.Errors);
    }

    [Fact]
    public async Task RegisterAsync_rejects_an_unsupported_frequency()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");

        var service = new RegisterMaintenanceScheduleService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterMaintenanceScheduleValidationException>(
            () => service.RegisterAsync(
                new RegisterMaintenanceScheduleRequest
                {
                    MaintenanceType = "Inspection",
                    Frequency = "every other tuesday",
                    NextDueDate = NextDueDate,
                    EquipmentIds = [equipment.Id],
                },
                CancellationToken.None));

        Assert.Contains(RegisterMaintenanceScheduleValidator.FrequencyField, exception.Errors);
    }

    [Fact]
    public async Task RegisterAsync_requires_equipment()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterMaintenanceScheduleService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterMaintenanceScheduleValidationException>(
            () => service.RegisterAsync(
                new RegisterMaintenanceScheduleRequest
                {
                    MaintenanceType = "Inspection",
                    Frequency = "Monthly",
                    NextDueDate = NextDueDate,
                    EquipmentIds = [],
                },
                CancellationToken.None));

        Assert.Contains(RegisterMaintenanceScheduleValidator.EquipmentIdsField, exception.Errors);
    }

    [Fact]
    public async Task RegisterAsync_rejects_duplicate_equipment_ids()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");

        var service = new RegisterMaintenanceScheduleService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterMaintenanceScheduleValidationException>(
            () => service.RegisterAsync(
                new RegisterMaintenanceScheduleRequest
                {
                    MaintenanceType = "Inspection",
                    Frequency = "Monthly",
                    NextDueDate = NextDueDate,
                    EquipmentIds = [equipment.Id, equipment.Id],
                },
                CancellationToken.None));

        Assert.Contains(RegisterMaintenanceScheduleValidator.EquipmentIdsField, exception.Errors);
    }

    [Fact]
    public async Task RegisterAsync_rejects_equipment_that_maintenance_does_not_own()
    {
        await using var dbContext = CreateDbContext();
        var known = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        var unknownEquipmentId = Guid.NewGuid();

        var service = new RegisterMaintenanceScheduleService(dbContext);

        var exception = await Assert.ThrowsAsync<MaintenanceScheduleEquipmentNotFoundException>(
            () => service.RegisterAsync(
                new RegisterMaintenanceScheduleRequest
                {
                    MaintenanceType = "Inspection",
                    Frequency = "Monthly",
                    NextDueDate = NextDueDate,
                    EquipmentIds = [known.Id, unknownEquipmentId],
                },
                CancellationToken.None));

        Assert.Equal([unknownEquipmentId], exception.EquipmentIds);
        Assert.Empty(dbContext.Set<MaintenanceSchedule>());
        Assert.Empty(dbContext.Set<ScheduleEquipment>());
    }

    [Fact]
    public async Task RegisterAsync_rejects_equipment_from_more_than_one_vessel()
    {
        await using var dbContext = CreateDbContext();
        var first = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        var second = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Boiler");

        var service = new RegisterMaintenanceScheduleService(dbContext);

        var exception = await Assert.ThrowsAsync<MaintenanceScheduleVesselConflictException>(
            () => service.RegisterAsync(
                new RegisterMaintenanceScheduleRequest
                {
                    MaintenanceType = "Inspection",
                    Frequency = "Monthly",
                    NextDueDate = NextDueDate,
                    EquipmentIds = [first.Id, second.Id],
                },
                CancellationToken.None));

        Assert.Equal(2, exception.VesselIds.Count);
        Assert.Empty(dbContext.Set<MaintenanceSchedule>());
        Assert.Empty(dbContext.Set<ScheduleEquipment>());
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

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
