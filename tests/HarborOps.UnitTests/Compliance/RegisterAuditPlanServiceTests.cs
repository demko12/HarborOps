using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class RegisterAuditPlanServiceTests
{
    [Fact]
    public async Task RegisterAsync_persists_a_new_plan_in_the_scheduled_status()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();

        var response = await new RegisterAuditPlanService(dbContext).RegisterAsync(
            ValidRequest(vesselId), CancellationToken.None);

        var saved = await dbContext.Set<AuditPlan>().SingleAsync(plan => plan.Id == response.Id);
        Assert.Equal(vesselId, saved.VesselId);
        Assert.Equal(AuditPlan.ScheduledStatus, saved.Status);
        Assert.Equal(SurveyAudit.TypeAnnualSurvey, saved.Type);
    }

    [Fact]
    public async Task RegisterAsync_rejects_a_due_date_before_the_planned_start_date()
    {
        await using var dbContext = CreateDbContext();
        var request = ValidRequest(Guid.NewGuid()) with
        {
            PlannedStartDate = new DateOnly(2026, 9, 10),
            DueDate = new DateOnly(2026, 9, 1),
        };

        var exception = await Assert.ThrowsAsync<RegisterAuditPlanValidationException>(
            () => new RegisterAuditPlanService(dbContext).RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterAuditPlanValidator.DueDateField, exception.Errors);
    }

    [Fact]
    public async Task RegisterAsync_rejects_an_unsupported_type()
    {
        await using var dbContext = CreateDbContext();
        var request = ValidRequest(Guid.NewGuid()) with { Type = "Random check" };

        var exception = await Assert.ThrowsAsync<RegisterAuditPlanValidationException>(
            () => new RegisterAuditPlanService(dbContext).RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterAuditPlanValidator.TypeField, exception.Errors);
    }

    [Fact]
    public async Task RegisterAsync_rejects_a_scope_exceeding_the_max_length()
    {
        await using var dbContext = CreateDbContext();
        var request = ValidRequest(Guid.NewGuid()) with { Scope = new string('a', AuditPlan.ScopeMaxLength + 1) };

        var exception = await Assert.ThrowsAsync<RegisterAuditPlanValidationException>(
            () => new RegisterAuditPlanService(dbContext).RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterAuditPlanValidator.ScopeField, exception.Errors);
    }

    [Theory]
    [InlineData(2026, 8, 20, 2026, 9, 10, true)] // touches the existing plan's start date -> overlap
    [InlineData(2026, 9, 15, 2026, 9, 30, true)] // touches the existing plan's due date -> overlap
    [InlineData(2026, 9, 1, 2026, 9, 5, true)] // fully inside -> overlap
    [InlineData(2026, 8, 1, 2026, 8, 19, false)] // strictly before -> no overlap
    [InlineData(2026, 9, 16, 2026, 9, 30, false)] // strictly after -> no overlap
    public async Task RegisterAsync_applies_inclusive_overlap_boundaries(
        int startYear, int startMonth, int startDay,
        int dueYear, int dueMonth, int dueDay,
        bool expectOverlap)
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        await SeedPlanAsync(dbContext, vesselId, SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 8, 20), new DateOnly(2026, 9, 15));
        var request = ValidRequest(vesselId) with
        {
            PlannedStartDate = new DateOnly(startYear, startMonth, startDay),
            DueDate = new DateOnly(dueYear, dueMonth, dueDay),
        };

        if (expectOverlap)
        {
            await Assert.ThrowsAsync<OverlappingAuditPlanException>(
                () => new RegisterAuditPlanService(dbContext).RegisterAsync(request, CancellationToken.None));
        }
        else
        {
            var response = await new RegisterAuditPlanService(dbContext).RegisterAsync(request, CancellationToken.None);
            Assert.NotEqual(Guid.Empty, response.Id);
        }
    }

    [Fact]
    public async Task RegisterAsync_allows_overlapping_dates_for_a_different_audit_type()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        await SeedPlanAsync(dbContext, vesselId, SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15));
        var request = ValidRequest(vesselId) with { Type = SurveyAudit.TypeInternalAudit };

        var response = await new RegisterAuditPlanService(dbContext).RegisterAsync(request, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
    }

    [Fact]
    public async Task RegisterAsync_ignores_cancelled_plans_when_checking_for_overlap()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var existing = await SeedPlanAsync(dbContext, vesselId, SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15));
        existing.Cancel("No longer needed.", DateTime.UtcNow);
        await dbContext.SaveChangesAsync();

        var response = await new RegisterAuditPlanService(dbContext).RegisterAsync(ValidRequest(vesselId), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
    }

    private static RegisterAuditPlanRequest ValidRequest(Guid vesselId) => new()
    {
        VesselId = vesselId,
        Type = SurveyAudit.TypeAnnualSurvey,
        PlannedStartDate = new DateOnly(2026, 9, 1),
        DueDate = new DateOnly(2026, 9, 15),
        AssignedInspectorUserId = null,
        Scope = "Full hull and machinery survey.",
        Notes = null,
    };

    private static async Task<AuditPlan> SeedPlanAsync(
        HarborOpsDbContext dbContext, Guid vesselId, string type, DateOnly plannedStartDate, DateOnly dueDate)
    {
        var plan = AuditPlan.Create(vesselId, type, plannedStartDate, dueDate, null, "Scope.", null);
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
}
