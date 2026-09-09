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

public sealed class CancelAuditPlanEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public CancelAuditPlanEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_cancels_a_scheduled_plan()
    {
        var planId = await SeedScheduledPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/audit-plans/{planId}/cancel", new CancelAuditPlanRequest("Vessel sold."));
        var result = await response.Content.ReadFromJsonAsync<CancelAuditPlanResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(AuditPlan.CancelledStatus, result.Status);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var saved = await dbContext.Set<AuditPlan>().AsNoTracking().SingleAsync(plan => plan.Id == planId);
        Assert.Equal(AuditPlan.CancelledStatus, saved.Status);
        Assert.Equal("Vessel sold.", saved.CancellationReason);
    }

    [Fact]
    public async Task Post_returns_400_for_a_missing_reason()
    {
        var planId = await SeedScheduledPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/audit-plans/{planId}/cancel", new CancelAuditPlanRequest(null));
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(CancelAuditPlanValidator.ReasonField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_404_for_an_unknown_plan()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/audit-plans/{Guid.NewGuid()}/cancel", new CancelAuditPlanRequest("Reason."));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_409_once_the_plan_is_completed()
    {
        var planId = await SeedCompletedPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/audit-plans/{planId}/cancel", new CancelAuditPlanRequest("Too late."));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_403_with_read_only_permission()
    {
        var planId = await SeedScheduledPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/audit-plans/{planId}/cancel", new CancelAuditPlanRequest("Reason."));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> SeedScheduledPlanAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var plan = AuditPlan.Create(
            Guid.NewGuid(), SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), null, "Scope.", null);

        await dbContext.AddAsync(plan);
        await dbContext.SaveChangesAsync();

        return plan.Id;
    }

    private async Task<Guid> SeedCompletedPlanAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var plan = AuditPlan.Create(
            Guid.NewGuid(), SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), null, "Scope.", null);
        plan.Assign(Guid.NewGuid());
        plan.Start(DateTime.UtcNow);
        var survey = new SurveyAudit(plan.VesselId, new DateOnly(2026, 9, 14), plan.Type, SurveyAudit.ResultSatisfactory, "Fine.");
        plan.Complete(survey, DateTime.UtcNow);

        await dbContext.AddAsync(plan);
        await dbContext.SaveChangesAsync();

        return plan.Id;
    }
}
