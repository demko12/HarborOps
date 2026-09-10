using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetScheduleOccurrencesQueryTests
{
    private static readonly DateTime GeneratedUtc = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 9, 10);

    [Fact]
    public async Task GetOccurrencesAsync_lists_the_occurrences_by_due_date_with_their_links()
    {
        await using var dbContext = CreateDbContext();
        var schedule = await SeedScheduleAsync(dbContext, MaintenanceFrequency.Monthly, Today.AddDays(40));
        var other = await SeedScheduleAsync(dbContext, MaintenanceFrequency.Monthly, Today.AddDays(40));

        var completedWorkOrderId = Guid.NewGuid();
        var completed = ScheduleOccurrence.Generate(schedule.Id, Today.AddDays(10), completedWorkOrderId, GeneratedUtc);
        completed.MarkCompleted(completedWorkOrderId, GeneratedUtc.AddDays(10));

        var cancelledWorkOrderId = Guid.NewGuid();
        var replacementWorkOrderId = Guid.NewGuid();
        var replaced = ScheduleOccurrence.Generate(schedule.Id, Today.AddDays(-20), cancelledWorkOrderId, GeneratedUtc);
        replaced.MarkCancelled(cancelledWorkOrderId, GeneratedUtc);
        replaced.Replace(replacementWorkOrderId, GeneratedUtc.AddHours(1));

        var foreign = ScheduleOccurrence.Generate(other.Id, Today, Guid.NewGuid(), GeneratedUtc);

        await dbContext.Set<ScheduleOccurrence>().AddRangeAsync(completed, replaced, foreign);
        await dbContext.SaveChangesAsync();

        var response = await new GetScheduleOccurrencesQuery(dbContext)
            .GetOccurrencesAsync(schedule.Id, CancellationToken.None);

        Assert.Equal(schedule.Id, response.ScheduleId);
        Assert.Equal("Monthly", response.Frequency);
        Assert.Equal(Today.AddDays(40), response.NextDueDate);
        Assert.Equal([replaced.Id, completed.Id], response.Items.Select(item => item.OccurrenceId));

        var first = response.Items[0];

        Assert.Equal(Today.AddDays(-20), first.DueDate);
        Assert.Equal(new DateTime(2026, 8, 21, 23, 59, 59, DateTimeKind.Utc), first.DueUtc);
        Assert.Equal("Generated", first.State);
        Assert.Equal(replacementWorkOrderId, first.WorkOrderId);
        Assert.Equal(cancelledWorkOrderId, first.ReplacedWorkOrderId);

        var second = response.Items[1];

        Assert.Equal("Completed", second.State);
        Assert.Equal(completedWorkOrderId, second.WorkOrderId);
        Assert.Equal(GeneratedUtc.AddDays(10), second.ResolvedUtc);
    }

    [Fact]
    public async Task GetOccurrencesAsync_returns_an_empty_list_for_a_schedule_without_occurrences()
    {
        await using var dbContext = CreateDbContext();
        var schedule = await SeedScheduleAsync(dbContext, MaintenanceFrequency.Quarterly, Today.AddDays(5));

        var response = await new GetScheduleOccurrencesQuery(dbContext)
            .GetOccurrencesAsync(schedule.Id, CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal("Quarterly", response.Frequency);
    }

    [Fact]
    public async Task GetOccurrencesAsync_rejects_an_unknown_schedule()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<MaintenanceScheduleNotFoundException>(
            () => new GetScheduleOccurrencesQuery(dbContext).GetOccurrencesAsync(missingId, CancellationToken.None));

        Assert.Equal(missingId, exception.ScheduleId);
    }

    [Fact]
    public async Task GetOccurrencesAsync_rejects_an_hour_only_schedule()
    {
        await using var dbContext = CreateDbContext();
        var schedule = await SeedScheduleAsync(dbContext, MaintenanceFrequency.RunningHours, null);

        var exception = await Assert.ThrowsAsync<MaintenanceScheduleNotTimeBasedException>(
            () => new GetScheduleOccurrencesQuery(dbContext).GetOccurrencesAsync(schedule.Id, CancellationToken.None));

        Assert.Equal(schedule.Id, exception.ScheduleId);
        Assert.Equal("RunningHours", exception.Frequency);
    }

    private static async Task<MaintenanceSchedule> SeedScheduleAsync(
        HarborOpsDbContext dbContext,
        MaintenanceFrequency frequency,
        DateOnly? nextDueDate)
    {
        var schedule = MaintenanceSchedule.Create(
            "Inspection",
            frequency,
            nextDueDate,
            frequency is MaintenanceFrequency.RunningHours ? 800 : null);

        await dbContext.Set<MaintenanceSchedule>().AddAsync(schedule);
        await dbContext.SaveChangesAsync();

        return schedule;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
