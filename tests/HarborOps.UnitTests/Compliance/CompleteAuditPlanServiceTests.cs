using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class CompleteAuditPlanServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompleteAsync_creates_and_links_exactly_one_survey_audit()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedInProgressPlanAsync(dbContext);
        var actingUserId = Guid.NewGuid();

        var response = await CreateService(dbContext).CompleteAsync(
            plan.Id, actingUserId, ValidRequest(), CancellationToken.None);

        Assert.Equal(AuditPlan.CompletedStatus, (await dbContext.Set<AuditPlan>().SingleAsync(p => p.Id == plan.Id)).Status);
        Assert.Equal(1, await dbContext.Set<SurveyAudit>().CountAsync());

        var survey = await dbContext.Set<SurveyAudit>().SingleAsync(s => s.Id == response.SurveyAuditId);
        Assert.Equal(plan.VesselId, survey.VesselId);
        Assert.Equal(plan.Type, survey.Type);
        Assert.Equal(SurveyAudit.ResultSatisfactory, survey.Result);
        Assert.Equal("All requirements met.", survey.Findings);

        var updatedPlan = await dbContext.Set<AuditPlan>().SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(response.SurveyAuditId, updatedPlan.SurveyAuditId);

        var history = await dbContext.Set<AuditPlanHistory>()
            .SingleAsync(entry => entry.AuditPlanId == plan.Id && entry.EventType == AuditPlanHistory.CompletedEvent);
        Assert.Equal(actingUserId, history.ActorUserId);
        Assert.Equal(AuditPlan.InProgressStatus, history.PreviousStatus);
        Assert.Equal(AuditPlan.CompletedStatus, history.NewStatus);
    }

    [Fact]
    public async Task CompleteAsync_throws_when_the_plan_is_not_in_progress()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedScheduledPlanAsync(dbContext);

        await Assert.ThrowsAsync<InvalidAuditPlanTransitionException>(
            () => CreateService(dbContext).CompleteAsync(plan.Id, Guid.NewGuid(), ValidRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_throws_for_a_repeated_completion_and_creates_no_second_survey()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedInProgressPlanAsync(dbContext);
        await CreateService(dbContext).CompleteAsync(plan.Id, Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidAuditPlanTransitionException>(
            () => CreateService(dbContext).CompleteAsync(plan.Id, Guid.NewGuid(), ValidRequest(), CancellationToken.None));

        Assert.Equal(1, await dbContext.Set<SurveyAudit>().CountAsync());
        Assert.Equal(1, await dbContext.Set<AuditPlanHistory>()
            .CountAsync(entry => entry.AuditPlanId == plan.Id && entry.EventType == AuditPlanHistory.CompletedEvent));
    }

    [Fact]
    public async Task CompleteAsync_rejected_transition_leaves_no_trace()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedScheduledPlanAsync(dbContext);

        await Assert.ThrowsAsync<InvalidAuditPlanTransitionException>(
            () => CreateService(dbContext).CompleteAsync(plan.Id, Guid.NewGuid(), ValidRequest(), CancellationToken.None));

        Assert.Equal(0, await dbContext.Set<SurveyAudit>().CountAsync());
        Assert.Equal(0, await dbContext.Set<AuditPlanHistory>().CountAsync());
        var unchanged = await dbContext.Set<AuditPlan>().SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(AuditPlan.ScheduledStatus, unchanged.Status);
        Assert.Null(unchanged.SurveyAuditId);
    }

    [Fact]
    public async Task CompleteAsync_rejects_an_unsupported_result()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedInProgressPlanAsync(dbContext);
        var request = ValidRequest() with { Result = "Passed with comments" };

        var exception = await Assert.ThrowsAsync<CompleteAuditPlanValidationException>(
            () => CreateService(dbContext).CompleteAsync(plan.Id, Guid.NewGuid(), request, CancellationToken.None));

        Assert.Contains(CompleteAuditPlanValidator.ResultField, exception.Errors);
        Assert.Equal(0, await dbContext.Set<SurveyAudit>().CountAsync());
    }

    [Fact]
    public async Task CompleteAsync_rejects_a_future_actual_audit_date()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedInProgressPlanAsync(dbContext);
        var request = ValidRequest() with { ActualAuditDate = DateOnly.FromDateTime(FixedNow.UtcDateTime).AddDays(1) };

        var exception = await Assert.ThrowsAsync<CompleteAuditPlanValidationException>(
            () => CreateService(dbContext).CompleteAsync(plan.Id, Guid.NewGuid(), request, CancellationToken.None));

        Assert.Contains(CompleteAuditPlanValidator.ActualAuditDateField, exception.Errors);
    }

    private static CompleteAuditPlanService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedNow));

    private static CompleteAuditPlanRequest ValidRequest() => new()
    {
        Result = SurveyAudit.ResultSatisfactory,
        Findings = "All requirements met.",
        DocumentId = null,
        ActualAuditDate = new DateOnly(2026, 9, 15),
    };

    private static async Task<AuditPlan> SeedScheduledPlanAsync(HarborOpsDbContext dbContext)
    {
        var plan = AuditPlan.Create(
            Guid.NewGuid(), SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), null, "Scope.", null);
        await dbContext.AddAsync(plan);
        await dbContext.SaveChangesAsync();

        return plan;
    }

    private static async Task<AuditPlan> SeedInProgressPlanAsync(HarborOpsDbContext dbContext)
    {
        var plan = await SeedScheduledPlanAsync(dbContext);
        plan.Assign(Guid.NewGuid());
        plan.Start(FixedNow.UtcDateTime);
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
