using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class GetAuditPlansQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_filters_by_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var match = await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10));
        await SeedPlanAsync(dbContext, Guid.NewGuid(), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10));

        var response = await QueryAsync(dbContext, new GetAuditPlansRequest { VesselId = vesselId });

        Assert.Equal([match.Id], response.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetAsync_filters_by_type()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var annual = await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), SurveyAudit.TypeAnnualSurvey);
        await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), SurveyAudit.TypeInternalAudit);

        var response = await QueryAsync(dbContext, new GetAuditPlansRequest { Type = SurveyAudit.TypeAnnualSurvey });

        Assert.Equal([annual.Id], response.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetAsync_filters_by_status()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var scheduled = await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5));
        var cancelled = await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5));
        cancelled.Cancel("No longer needed.", FixedNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, new GetAuditPlansRequest { Status = AuditPlan.ScheduledStatus });

        Assert.Equal([scheduled.Id], response.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetAsync_returns_plans_intersecting_the_calendar_window()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var inside = await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 15));
        var spanning = await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5));

        var response = await QueryAsync(dbContext, new GetAuditPlansRequest
        {
            From = new DateOnly(2026, 9, 8),
            To = new DateOnly(2026, 9, 20),
        });

        var ids = response.Items.Select(item => item.Id).ToArray();
        Assert.Contains(inside.Id, ids);
        Assert.Contains(spanning.Id, ids);
        Assert.Equal(2, ids.Length);
    }

    [Fact]
    public async Task GetAsync_marks_an_overdue_scheduled_plan()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var overdue = await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10));
        var upcoming = await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));

        var response = await QueryAsync(dbContext, new GetAuditPlansRequest());

        Assert.True(response.Items.Single(item => item.Id == overdue.Id).IsOverdue);
        Assert.False(response.Items.Single(item => item.Id == upcoming.Id).IsOverdue);
    }

    [Fact]
    public async Task GetAsync_does_not_mark_a_completed_plan_with_a_past_due_date_as_overdue()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var plan = await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10));
        plan.Assign(Guid.NewGuid());
        plan.Start(FixedNow.UtcDateTime);
        var survey = new SurveyAudit(vesselId, new DateOnly(2026, 8, 9), plan.Type, SurveyAudit.ResultSatisfactory, "Fine.");
        plan.Complete(survey, FixedNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, new GetAuditPlansRequest());

        Assert.False(response.Items.Single(item => item.Id == plan.Id).IsOverdue);
    }

    [Fact]
    public async Task GetAsync_orders_by_planned_start_date_then_id()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var later = await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 15));
        var earlier = await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5));

        var response = await QueryAsync(dbContext, new GetAuditPlansRequest { PageSize = 100 });

        Assert.Equal(earlier.Id, response.Items[0].Id);
        Assert.Equal(later.Id, response.Items[1].Id);
    }

    [Fact]
    public async Task GetAsync_pages_results_and_reports_total_count_before_paging()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            await SeedPlanAsync(dbContext, vesselId, new DateOnly(2026, 9, 1 + i), new DateOnly(2026, 9, 5 + i));
        }

        var response = await QueryAsync(dbContext, new GetAuditPlansRequest { PageNumber = 2, PageSize = 2 });

        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal(2, response.PageNumber);
    }

    [Fact]
    public async Task GetAsync_rejects_an_unsupported_type()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetAuditPlansValidationException>(
            () => QueryAsync(dbContext, new GetAuditPlansRequest { Type = "Random check" }));

        Assert.Contains(GetAuditPlansValidator.TypeField, exception.Errors);
    }

    [Fact]
    public async Task GetAsync_rejects_a_from_date_after_the_to_date()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetAuditPlansValidationException>(
            () => QueryAsync(dbContext, new GetAuditPlansRequest { From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 9, 1) }));

        Assert.Contains(GetAuditPlansValidator.DateRangeField, exception.Errors);
    }

    private static Task<GetAuditPlansResponse> QueryAsync(HarborOpsDbContext dbContext, GetAuditPlansRequest request) =>
        new GetAuditPlansQuery(dbContext, new FixedTimeProvider(FixedNow)).GetAsync(request, CancellationToken.None);

    private static async Task<AuditPlan> SeedPlanAsync(
        HarborOpsDbContext dbContext, Guid vesselId, DateOnly plannedStartDate, DateOnly dueDate, string? type = null)
    {
        var plan = AuditPlan.Create(
            vesselId, type ?? SurveyAudit.TypeAnnualSurvey, plannedStartDate, dueDate, null, "Scope.", null);
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
