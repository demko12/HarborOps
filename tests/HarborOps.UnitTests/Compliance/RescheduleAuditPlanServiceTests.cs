using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class RescheduleAuditPlanServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RescheduleAsync_updates_dates_and_records_history()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15));
        var actingUserId = Guid.NewGuid();
        var newStart = new DateOnly(2026, 9, 10);
        var newDue = new DateOnly(2026, 9, 20);

        var response = await CreateService(dbContext).RescheduleAsync(
            plan.Id, actingUserId, new RescheduleAuditPlanRequest { PlannedStartDate = newStart, DueDate = newDue, Reason = "Yard availability changed." },
            CancellationToken.None);

        Assert.Equal(newStart, response.PlannedStartDate);
        Assert.Equal(newDue, response.DueDate);

        var history = await dbContext.Set<AuditPlanHistory>().SingleAsync(entry => entry.AuditPlanId == plan.Id);
        Assert.Equal(AuditPlanHistory.RescheduledEvent, history.EventType);
        Assert.Equal("Yard availability changed.", history.Reason);
        Assert.Equal(actingUserId, history.ActorUserId);
        Assert.Equal(FixedNow.UtcDateTime, history.OccurredAtUtc);
        Assert.Equal(new DateOnly(2026, 9, 1), history.PreviousPlannedStartDate);
        Assert.Equal(newStart, history.NewPlannedStartDate);
        Assert.Equal(new DateOnly(2026, 9, 15), history.PreviousDueDate);
        Assert.Equal(newDue, history.NewDueDate);
    }

    [Fact]
    public async Task RescheduleAsync_rejects_a_missing_reason()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15));

        var exception = await Assert.ThrowsAsync<RescheduleAuditPlanValidationException>(
            () => CreateService(dbContext).RescheduleAsync(
                plan.Id, Guid.NewGuid(), new RescheduleAuditPlanRequest { PlannedStartDate = new DateOnly(2026, 9, 5), DueDate = new DateOnly(2026, 9, 20) },
                CancellationToken.None));

        Assert.Contains(RescheduleAuditPlanValidator.ReasonField, exception.Errors);
    }

    [Fact]
    public async Task RescheduleAsync_throws_for_an_unknown_plan()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<AuditPlanNotFoundException>(
            () => CreateService(dbContext).RescheduleAsync(
                Guid.NewGuid(), Guid.NewGuid(),
                new RescheduleAuditPlanRequest { PlannedStartDate = new DateOnly(2026, 9, 5), DueDate = new DateOnly(2026, 9, 20), Reason = "Reason." },
                CancellationToken.None));

        Assert.NotEqual(Guid.Empty, exception.AuditPlanId);
    }

    [Fact]
    public async Task RescheduleAsync_throws_once_the_plan_has_started()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15));
        plan.Assign(Guid.NewGuid());
        plan.Start(FixedNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidAuditPlanTransitionException>(
            () => CreateService(dbContext).RescheduleAsync(
                plan.Id, Guid.NewGuid(),
                new RescheduleAuditPlanRequest { PlannedStartDate = new DateOnly(2026, 9, 5), DueDate = new DateOnly(2026, 9, 20), Reason = "Reason." },
                CancellationToken.None));
    }

    [Fact]
    public async Task RescheduleAsync_rejects_dates_overlapping_another_active_plan()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var plan = await SeedPlanAsync(dbContext, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10), vesselId);
        await SeedPlanAsync(dbContext, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 30), vesselId);

        await Assert.ThrowsAsync<OverlappingAuditPlanException>(
            () => CreateService(dbContext).RescheduleAsync(
                plan.Id, Guid.NewGuid(),
                new RescheduleAuditPlanRequest { PlannedStartDate = new DateOnly(2026, 9, 25), DueDate = new DateOnly(2026, 9, 28), Reason = "Reason." },
                CancellationToken.None));
    }

    private static RescheduleAuditPlanService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedNow));

    private static async Task<AuditPlan> SeedPlanAsync(
        HarborOpsDbContext dbContext, DateOnly plannedStartDate, DateOnly dueDate, Guid? vesselId = null)
    {
        var plan = AuditPlan.Create(
            vesselId ?? Guid.NewGuid(), SurveyAudit.TypeAnnualSurvey, plannedStartDate, dueDate, null, "Scope.", null);
        await dbContext.AddAsync(plan);
        await dbContext.SaveChangesAsync();

        return plan;
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
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
