using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class RescheduleAuditPlanEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public RescheduleAuditPlanEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Put_reschedules_a_scheduled_plan()
    {
        var planId = await SeedPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var newStart = new DateOnly(2026, 10, 10);
        var newDue = new DateOnly(2026, 10, 20);

        using var response = await client.PutAsJsonAsync(
            $"/api/compliance/audit-plans/{planId}/schedule",
            new RescheduleAuditPlanRequest { PlannedStartDate = newStart, DueDate = newDue, Reason = "Yard slot moved." });
        var result = await response.Content.ReadFromJsonAsync<RescheduleAuditPlanResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(newStart, result.PlannedStartDate);
        Assert.Equal(newDue, result.DueDate);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var history = await dbContext.Set<AuditPlanHistory>().AsNoTracking().SingleAsync(entry => entry.AuditPlanId == planId);
        Assert.Equal(AuditPlanHistory.RescheduledEvent, history.EventType);
    }

    [Fact]
    public async Task Put_returns_400_for_a_missing_reason()
    {
        var planId = await SeedPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PutAsJsonAsync(
            $"/api/compliance/audit-plans/{planId}/schedule",
            new RescheduleAuditPlanRequest { PlannedStartDate = new DateOnly(2026, 10, 10), DueDate = new DateOnly(2026, 10, 20) });
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RescheduleAuditPlanValidator.ReasonField, problem.Errors);
    }

    [Fact]
    public async Task Put_returns_404_for_an_unknown_plan()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PutAsJsonAsync(
            $"/api/compliance/audit-plans/{Guid.NewGuid()}/schedule",
            new RescheduleAuditPlanRequest { PlannedStartDate = new DateOnly(2026, 10, 10), DueDate = new DateOnly(2026, 10, 20), Reason = "Reason." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_409_once_the_plan_has_started()
    {
        var planId = await SeedInProgressPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PutAsJsonAsync(
            $"/api/compliance/audit-plans/{planId}/schedule",
            new RescheduleAuditPlanRequest { PlannedStartDate = new DateOnly(2026, 10, 10), DueDate = new DateOnly(2026, 10, 20), Reason = "Reason." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_403_with_read_only_permission()
    {
        var planId = await SeedPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);

        using var response = await client.PutAsJsonAsync(
            $"/api/compliance/audit-plans/{planId}/schedule",
            new RescheduleAuditPlanRequest { PlannedStartDate = new DateOnly(2026, 10, 10), DueDate = new DateOnly(2026, 10, 20), Reason = "Reason." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> SeedPlanAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var plan = AuditPlan.Create(
            Guid.NewGuid(), SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), null, "Scope.", null);

        await dbContext.AddAsync(plan);
        await dbContext.SaveChangesAsync();

        return plan.Id;
    }

    private async Task<Guid> SeedInProgressPlanAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var plan = AuditPlan.Create(
            Guid.NewGuid(), SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), null, "Scope.", null);
        plan.Assign(Guid.NewGuid());
        plan.Start(DateTime.UtcNow);

        await dbContext.AddAsync(plan);
        await dbContext.SaveChangesAsync();

        return plan.Id;
    }
}
