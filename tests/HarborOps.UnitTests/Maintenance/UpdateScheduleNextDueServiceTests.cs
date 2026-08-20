using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class UpdateScheduleNextDueServiceTests
{
    private static readonly DateOnly CompletedOn = new(2026, 8, 18);

    [Fact]
    public async Task UpdateNextDueAsync_advances_the_date_from_the_stored_frequency()
    {
        await using var dbContext = CreateDbContext();
        var scheduleId = await SeedScheduleAsync(
            dbContext,
            MaintenanceFrequency.Quarterly,
            new DateOnly(2026, 8, 1),
            null);

        var service = new UpdateScheduleNextDueService(dbContext);

        var response = await service.UpdateNextDueAsync(
            scheduleId,
            new UpdateScheduleNextDueRequest { CompletedOn = CompletedOn },
            CancellationToken.None);

        var stored = await dbContext.Set<MaintenanceSchedule>().SingleAsync();

        Assert.Equal(new DateOnly(2026, 11, 18), response.NextDueDate);
        Assert.Equal(new DateOnly(2026, 11, 18), stored.NextDueDate);
        Assert.Equal("Quarterly", response.Frequency);
    }

    [Fact]
    public async Task UpdateNextDueAsync_updates_the_hours_of_a_running_hours_schedule()
    {
        await using var dbContext = CreateDbContext();
        var scheduleId = await SeedScheduleAsync(dbContext, MaintenanceFrequency.RunningHours, null, 500);

        var service = new UpdateScheduleNextDueService(dbContext);

        var response = await service.UpdateNextDueAsync(
            scheduleId,
            new UpdateScheduleNextDueRequest { CompletedOn = CompletedOn, NextDueHours = 1000 },
            CancellationToken.None);

        Assert.Null(response.NextDueDate);
        Assert.Equal(1000, response.NextDueHours);
    }

    [Fact]
    public async Task UpdateNextDueAsync_requires_hours_for_a_running_hours_schedule()
    {
        await using var dbContext = CreateDbContext();
        var scheduleId = await SeedScheduleAsync(dbContext, MaintenanceFrequency.RunningHours, null, 500);

        var service = new UpdateScheduleNextDueService(dbContext);

        var exception = await Assert.ThrowsAsync<UpdateScheduleNextDueValidationException>(
            () => service.UpdateNextDueAsync(
                scheduleId,
                new UpdateScheduleNextDueRequest { CompletedOn = CompletedOn },
                CancellationToken.None));

        Assert.Contains(UpdateScheduleNextDueValidator.NextDueHoursField, exception.Errors);
    }

    [Fact]
    public async Task UpdateNextDueAsync_requires_a_completion_date()
    {
        await using var dbContext = CreateDbContext();
        var scheduleId = await SeedScheduleAsync(
            dbContext,
            MaintenanceFrequency.Monthly,
            new DateOnly(2026, 8, 1),
            null);

        var service = new UpdateScheduleNextDueService(dbContext);

        var exception = await Assert.ThrowsAsync<UpdateScheduleNextDueValidationException>(
            () => service.UpdateNextDueAsync(
                scheduleId,
                new UpdateScheduleNextDueRequest(),
                CancellationToken.None));

        Assert.Contains(UpdateScheduleNextDueValidator.CompletedOnField, exception.Errors);
    }

    [Fact]
    public async Task UpdateNextDueAsync_rejects_negative_hours()
    {
        await using var dbContext = CreateDbContext();
        var scheduleId = await SeedScheduleAsync(
            dbContext,
            MaintenanceFrequency.Monthly,
            new DateOnly(2026, 8, 1),
            null);

        var service = new UpdateScheduleNextDueService(dbContext);

        var exception = await Assert.ThrowsAsync<UpdateScheduleNextDueValidationException>(
            () => service.UpdateNextDueAsync(
                scheduleId,
                new UpdateScheduleNextDueRequest { CompletedOn = CompletedOn, NextDueHours = -5 },
                CancellationToken.None));

        Assert.Contains(UpdateScheduleNextDueValidator.NextDueHoursField, exception.Errors);
    }

    [Fact]
    public async Task UpdateNextDueAsync_reports_a_stored_frequency_outside_the_supported_set()
    {
        await using var dbContext = CreateDbContext();
        var scheduleId = await SeedScheduleAsync(
            dbContext,
            MaintenanceFrequency.SemiAnnual,
            new DateOnly(2026, 8, 23),
            12000);

        var stored = await dbContext.Set<MaintenanceSchedule>().SingleAsync();
        dbContext.Entry(stored).Property(nameof(MaintenanceSchedule.Frequency)).CurrentValue = "Every 6 months";
        await dbContext.SaveChangesAsync();

        var service = new UpdateScheduleNextDueService(dbContext);

        var exception = await Assert.ThrowsAsync<UpdateScheduleNextDueValidationException>(
            () => service.UpdateNextDueAsync(
                scheduleId,
                new UpdateScheduleNextDueRequest { CompletedOn = CompletedOn },
                CancellationToken.None));

        Assert.Contains(UpdateScheduleNextDueValidator.FrequencyField, exception.Errors);
        Assert.Contains(
            "Every 6 months",
            exception.Errors[UpdateScheduleNextDueValidator.FrequencyField][0],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateNextDueAsync_throws_when_the_schedule_does_not_exist()
    {
        await using var dbContext = CreateDbContext();
        var scheduleId = Guid.NewGuid();

        var service = new UpdateScheduleNextDueService(dbContext);

        var exception = await Assert.ThrowsAsync<MaintenanceScheduleNotFoundException>(
            () => service.UpdateNextDueAsync(
                scheduleId,
                new UpdateScheduleNextDueRequest { CompletedOn = CompletedOn },
                CancellationToken.None));

        Assert.Equal(scheduleId, exception.ScheduleId);
    }

    private static async Task<Guid> SeedScheduleAsync(
        HarborOpsDbContext dbContext,
        MaintenanceFrequency frequency,
        DateOnly? nextDueDate,
        int? nextDueHours)
    {
        var schedule = MaintenanceSchedule.Create("Inspection", frequency, nextDueDate, nextDueHours);

        await dbContext.Set<MaintenanceSchedule>().AddAsync(schedule);
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
}
