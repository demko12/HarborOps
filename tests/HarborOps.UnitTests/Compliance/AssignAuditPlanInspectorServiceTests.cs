using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class AssignAuditPlanInspectorServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AssignAsync_sets_the_inspector_while_scheduled_and_records_history()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        var actingUserId = Guid.NewGuid();
        var inspectorId = Guid.NewGuid();

        var response = await CreateService(dbContext).AssignAsync(
            plan.Id, actingUserId, new AssignAuditPlanInspectorRequest { InspectorUserId = inspectorId, Reason = "Primary auditor available." },
            CancellationToken.None);

        Assert.Equal(inspectorId, response.InspectorUserId);

        var history = await dbContext.Set<AuditPlanHistory>().SingleAsync(entry => entry.AuditPlanId == plan.Id);
        Assert.Equal(AuditPlanHistory.AssignedEvent, history.EventType);
        Assert.Null(history.PreviousInspectorUserId);
        Assert.Equal(inspectorId, history.NewInspectorUserId);
        Assert.Equal(actingUserId, history.ActorUserId);
        Assert.Equal(FixedNow.UtcDateTime, history.OccurredAtUtc);
    }

    [Fact]
    public async Task AssignAsync_reassigns_while_in_progress_and_records_the_previous_inspector()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        var firstInspector = Guid.NewGuid();
        plan.Assign(firstInspector);
        plan.Start(FixedNow.UtcDateTime);
        await dbContext.SaveChangesAsync();
        var secondInspector = Guid.NewGuid();

        await CreateService(dbContext).AssignAsync(
            plan.Id, Guid.NewGuid(), new AssignAuditPlanInspectorRequest { InspectorUserId = secondInspector }, CancellationToken.None);

        var history = await dbContext.Set<AuditPlanHistory>()
            .SingleAsync(entry => entry.AuditPlanId == plan.Id && entry.EventType == AuditPlanHistory.AssignedEvent);
        Assert.Equal(firstInspector, history.PreviousInspectorUserId);
        Assert.Equal(secondInspector, history.NewInspectorUserId);
        Assert.Null(history.Reason);
    }

    [Fact]
    public async Task AssignAsync_rejects_a_missing_inspector()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);

        var exception = await Assert.ThrowsAsync<AssignAuditPlanInspectorValidationException>(
            () => CreateService(dbContext).AssignAsync(
                plan.Id, Guid.NewGuid(), new AssignAuditPlanInspectorRequest(), CancellationToken.None));

        Assert.Contains(AssignAuditPlanInspectorValidator.InspectorUserIdField, exception.Errors);
    }

    [Fact]
    public async Task AssignAsync_throws_for_an_unknown_plan()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<AuditPlanNotFoundException>(
            () => CreateService(dbContext).AssignAsync(
                Guid.NewGuid(), Guid.NewGuid(), new AssignAuditPlanInspectorRequest { InspectorUserId = Guid.NewGuid() }, CancellationToken.None));
    }

    [Fact]
    public async Task AssignAsync_throws_once_the_plan_is_completed()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        plan.Assign(Guid.NewGuid());
        plan.Start(FixedNow.UtcDateTime);
        var survey = new SurveyAudit(plan.VesselId, new DateOnly(2026, 9, 10), plan.Type, SurveyAudit.ResultSatisfactory, "All clear.");
        plan.Complete(survey, FixedNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidAuditPlanTransitionException>(
            () => CreateService(dbContext).AssignAsync(
                plan.Id, Guid.NewGuid(), new AssignAuditPlanInspectorRequest { InspectorUserId = Guid.NewGuid() }, CancellationToken.None));
    }

    private static AssignAuditPlanInspectorService CreateService(HarborOpsDbContext dbContext) =>
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
