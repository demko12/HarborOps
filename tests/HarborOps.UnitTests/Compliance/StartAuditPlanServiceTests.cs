using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class StartAuditPlanServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartAsync_moves_an_assigned_plan_to_in_progress_and_records_history()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        plan.Assign(Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var actingUserId = Guid.NewGuid();

        var response = await CreateService(dbContext).StartAsync(plan.Id, actingUserId, CancellationToken.None);

        Assert.Equal(AuditPlan.InProgressStatus, response.Status);
        Assert.Equal(FixedNow.UtcDateTime, response.StartedAtUtc);

        var history = await dbContext.Set<AuditPlanHistory>().SingleAsync(entry => entry.AuditPlanId == plan.Id);
        Assert.Equal(AuditPlanHistory.StartedEvent, history.EventType);
        Assert.Equal(AuditPlan.ScheduledStatus, history.PreviousStatus);
        Assert.Equal(AuditPlan.InProgressStatus, history.NewStatus);
        Assert.Equal(actingUserId, history.ActorUserId);
    }

    [Fact]
    public async Task StartAsync_throws_when_no_inspector_is_assigned()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);

        var exception = await Assert.ThrowsAsync<InvalidAuditPlanTransitionException>(
            () => CreateService(dbContext).StartAsync(plan.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(AuditPlan.StartAction, exception.AttemptedAction);
    }

    [Fact]
    public async Task StartAsync_throws_when_the_plan_is_not_scheduled()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        plan.Assign(Guid.NewGuid());
        plan.Start(FixedNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidAuditPlanTransitionException>(
            () => CreateService(dbContext).StartAsync(plan.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_throws_for_an_unknown_plan()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<AuditPlanNotFoundException>(
            () => CreateService(dbContext).StartAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    private static StartAuditPlanService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedNow));

    private static async Task<AuditPlan> SeedPlanAsync(HarborOpsDbContext dbContext)
    {
        var plan = AuditPlan.Create(
            Guid.NewGuid(), SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), null, "Scope.", null);
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
