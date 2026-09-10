using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class CancelAuditPlanServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CancelAsync_cancels_a_scheduled_plan_and_records_history()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        var actingUserId = Guid.NewGuid();

        var response = await CreateService(dbContext).CancelAsync(
            plan.Id, actingUserId, new CancelAuditPlanRequest("Vessel sold."), CancellationToken.None);

        Assert.Equal(AuditPlan.CancelledStatus, response.Status);
        Assert.Equal(FixedNow.UtcDateTime, response.CancelledAtUtc);

        var history = await dbContext.Set<AuditPlanHistory>().SingleAsync(entry => entry.AuditPlanId == plan.Id);
        Assert.Equal(AuditPlanHistory.CancelledEvent, history.EventType);
        Assert.Equal("Vessel sold.", history.Reason);
        Assert.Equal(actingUserId, history.ActorUserId);
    }

    [Fact]
    public async Task CancelAsync_cancels_an_in_progress_plan()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        plan.Assign(Guid.NewGuid());
        plan.Start(FixedNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        var response = await CreateService(dbContext).CancelAsync(
            plan.Id, Guid.NewGuid(), new CancelAuditPlanRequest("Deferred."), CancellationToken.None);

        Assert.Equal(AuditPlan.CancelledStatus, response.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CancelAsync_rejects_a_missing_reason(string? reason)
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);

        var exception = await Assert.ThrowsAsync<CancelAuditPlanValidationException>(
            () => CreateService(dbContext).CancelAsync(plan.Id, Guid.NewGuid(), new CancelAuditPlanRequest(reason), CancellationToken.None));

        Assert.Contains(CancelAuditPlanValidator.ReasonField, exception.Errors);
    }

    [Fact]
    public async Task CancelAsync_throws_for_an_unknown_plan()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<AuditPlanNotFoundException>(
            () => CreateService(dbContext).CancelAsync(Guid.NewGuid(), Guid.NewGuid(), new CancelAuditPlanRequest("Reason."), CancellationToken.None));
    }

    [Fact]
    public async Task CancelAsync_throws_once_the_plan_is_completed()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        plan.Assign(Guid.NewGuid());
        plan.Start(FixedNow.UtcDateTime);
        var survey = new SurveyAudit(plan.VesselId, new DateOnly(2026, 9, 15), plan.Type, SurveyAudit.ResultSatisfactory, "All clear.");
        plan.Complete(survey, FixedNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidAuditPlanTransitionException>(
            () => CreateService(dbContext).CancelAsync(plan.Id, Guid.NewGuid(), new CancelAuditPlanRequest("Too late."), CancellationToken.None));
    }

    private static CancelAuditPlanService CreateService(HarborOpsDbContext dbContext) =>
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
