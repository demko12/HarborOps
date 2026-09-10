using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class GetAuditPlanDetailQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_plan()
    {
        await using var dbContext = CreateDbContext();

        var response = await QueryAsync(dbContext, Guid.NewGuid());

        Assert.Null(response);
    }

    [Fact]
    public async Task GetAsync_projects_core_fields()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);

        var response = await QueryAsync(dbContext, plan.Id);

        Assert.NotNull(response);
        Assert.Equal(plan.VesselId, response.VesselId);
        Assert.Equal(plan.Type, response.Type);
        Assert.Equal(plan.Status, response.Status);
        Assert.Equal(plan.Scope, response.Scope);
        Assert.Null(response.SurveyAuditId);
        Assert.Empty(response.History);
    }

    [Fact]
    public async Task GetAsync_orders_history_newest_first()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        var older = AuditPlanHistory.RecordAssigned(
            plan.Id, "First.", Guid.NewGuid(), FixedNow.UtcDateTime.AddDays(-2), plan.Status, null, Guid.NewGuid());
        var newer = AuditPlanHistory.RecordAssigned(
            plan.Id, "Second.", Guid.NewGuid(), FixedNow.UtcDateTime.AddDays(-1), plan.Status, Guid.NewGuid(), Guid.NewGuid());
        await dbContext.AddRangeAsync(older, newer);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, plan.Id);

        Assert.NotNull(response);
        Assert.Equal(2, response.History.Count);
        Assert.Equal(newer.Id, response.History[0].Id);
        Assert.Equal(older.Id, response.History[1].Id);
    }

    [Fact]
    public async Task GetAsync_exposes_the_linked_survey_audit_id_after_completion()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        plan.Assign(Guid.NewGuid());
        plan.Start(FixedNow.UtcDateTime);
        var survey = new SurveyAudit(plan.VesselId, new DateOnly(2026, 9, 19), plan.Type, SurveyAudit.ResultSatisfactory, "Fine.");
        plan.Complete(survey, FixedNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, plan.Id);

        Assert.NotNull(response);
        Assert.Equal(survey.Id, response.SurveyAuditId);
    }

    private static Task<HarborOps.Modules.Compliance.Contracts.GetAuditPlanDetailResponse?> QueryAsync(
        HarborOpsDbContext dbContext, Guid auditPlanId) =>
        new GetAuditPlanDetailQuery(dbContext, new FixedTimeProvider(FixedNow)).GetAsync(auditPlanId, CancellationToken.None);

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
