using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetScheduleWorkOrdersQueryTests
{
    private static readonly DateTime GeneratedUtc = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 9, 10);

    [Fact]
    public async Task GetWorkOrdersAsync_lists_the_work_orders_generated_from_the_schedule()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var schedule = await SeedScheduleAsync(dbContext);
        var other = await SeedScheduleAsync(dbContext);

        var open = await SeedGeneratedAsync(dbContext, schedule, vesselId, Today.AddDays(5), complete: false);
        var done = await SeedGeneratedAsync(dbContext, schedule, vesselId, Today.AddDays(-3), complete: true);
        await SeedGeneratedAsync(dbContext, other, vesselId, Today.AddDays(1), complete: false);
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(MaintenanceWorkOrder.Create(
            vesselId,
            "Manual repair",
            null,
            WorkOrderPriority.High,
            GeneratedUtc.AddDays(2)));
        await dbContext.SaveChangesAsync();

        var response = await new GetScheduleWorkOrdersQuery(dbContext)
            .GetWorkOrdersAsync(schedule.Id, CancellationToken.None);

        Assert.Equal(schedule.Id, response.ScheduleId);
        Assert.Equal([done.Id, open.Id], response.Items.Select(item => item.WorkOrderId));

        var completed = response.Items[0];

        Assert.Equal(Today.AddDays(-3), completed.OccurrenceDueDate);
        Assert.Equal("Completed", completed.Status);
        Assert.Equal("Completed", completed.OccurrenceState);
        Assert.Equal(GeneratedUtc, completed.CompletedUtc);

        var pending = response.Items[1];

        Assert.Equal(vesselId, pending.VesselId);
        Assert.Equal("Open", pending.Status);
        Assert.Equal("Medium", pending.Priority);
        Assert.Equal("Generated", pending.OccurrenceState);
        Assert.Equal(new DateTime(2026, 9, 15, 23, 59, 59, DateTimeKind.Utc), pending.DueUtc);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_lists_the_replacement_for_a_replaced_occurrence()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var schedule = await SeedScheduleAsync(dbContext);
        var dueDate = Today.AddDays(5);

        var cancelled = MaintenanceWorkOrder.CreateFromSchedule(
            vesselId, "Preventive work", null, WorkOrderPriority.Medium, schedule.Id, dueDate,
            GeneratedUtc);
        var replacement = MaintenanceWorkOrder.CreateFromSchedule(
            vesselId, "Preventive work", null, WorkOrderPriority.Medium, schedule.Id, dueDate,
            GeneratedUtc);
        var occurrence = ScheduleOccurrence.Generate(schedule.Id, dueDate, cancelled.Id, GeneratedUtc);
        cancelled.Cancel();
        occurrence.MarkCancelled(cancelled.Id, GeneratedUtc);
        occurrence.Replace(replacement.Id, GeneratedUtc.AddHours(1));

        await dbContext.Set<MaintenanceWorkOrder>().AddRangeAsync(cancelled, replacement);
        await dbContext.Set<ScheduleOccurrence>().AddAsync(occurrence);
        await dbContext.SaveChangesAsync();

        var response = await new GetScheduleWorkOrdersQuery(dbContext)
            .GetWorkOrdersAsync(schedule.Id, CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(replacement.Id, item.WorkOrderId);
        Assert.Equal("Open", item.Status);
        Assert.Equal("Generated", item.OccurrenceState);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_returns_an_empty_list_for_a_schedule_without_generated_work()
    {
        await using var dbContext = CreateDbContext();
        var schedule = await SeedScheduleAsync(dbContext);

        var response = await new GetScheduleWorkOrdersQuery(dbContext)
            .GetWorkOrdersAsync(schedule.Id, CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_rejects_an_unknown_schedule()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<MaintenanceScheduleNotFoundException>(
            () => new GetScheduleWorkOrdersQuery(dbContext).GetWorkOrdersAsync(missingId, CancellationToken.None));

        Assert.Equal(missingId, exception.ScheduleId);
    }

    private static async Task<MaintenanceSchedule> SeedScheduleAsync(HarborOpsDbContext dbContext)
    {
        var schedule = MaintenanceSchedule.Create("Inspection", MaintenanceFrequency.Monthly, Today.AddDays(40), null);

        await dbContext.Set<MaintenanceSchedule>().AddAsync(schedule);
        await dbContext.SaveChangesAsync();

        return schedule;
    }

    private static async Task<MaintenanceWorkOrder> SeedGeneratedAsync(
        HarborOpsDbContext dbContext,
        MaintenanceSchedule schedule,
        Guid vesselId,
        DateOnly dueDate,
        bool complete)
    {
        var workOrder = MaintenanceWorkOrder.CreateFromSchedule(
            vesselId,
            "Preventive work",
            null,
            WorkOrderPriority.Medium,
            schedule.Id,
            dueDate,
            GeneratedUtc);
        var occurrence = ScheduleOccurrence.Generate(schedule.Id, dueDate, workOrder.Id, GeneratedUtc);

        if (complete)
        {
            workOrder.Complete(GeneratedUtc);
            occurrence.MarkCompleted(workOrder.Id, GeneratedUtc);
        }

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<ScheduleOccurrence>().AddAsync(occurrence);
        await dbContext.SaveChangesAsync();

        return workOrder;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
