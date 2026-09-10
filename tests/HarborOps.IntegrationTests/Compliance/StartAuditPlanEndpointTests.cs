using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class StartAuditPlanEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public StartAuditPlanEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_starts_an_assigned_scheduled_plan()
    {
        var planId = await SeedAssignedPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsync($"/api/compliance/audit-plans/{planId}/start", null, CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<StartAuditPlanResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(AuditPlan.InProgressStatus, result.Status);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var saved = await dbContext.Set<AuditPlan>().AsNoTracking().SingleAsync(plan => plan.Id == planId);
        Assert.Equal(AuditPlan.InProgressStatus, saved.Status);
    }

    [Fact]
    public async Task Post_returns_409_without_an_assigned_inspector()
    {
        var planId = await SeedUnassignedPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsync($"/api/compliance/audit-plans/{planId}/start", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_404_for_an_unknown_plan()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsync($"/api/compliance/audit-plans/{Guid.NewGuid()}/start", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_403_with_read_only_permission()
    {
        var planId = await SeedAssignedPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);

        using var response = await client.PostAsync($"/api/compliance/audit-plans/{planId}/start", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> SeedAssignedPlanAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var plan = AuditPlan.Create(
            Guid.NewGuid(), SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), null, "Scope.", null);
        plan.Assign(Guid.NewGuid());

        await dbContext.AddAsync(plan);
        await dbContext.SaveChangesAsync();

        return plan.Id;
    }

    private async Task<Guid> SeedUnassignedPlanAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var plan = AuditPlan.Create(
            Guid.NewGuid(), SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), null, "Scope.", null);

        await dbContext.AddAsync(plan);
        await dbContext.SaveChangesAsync();

        return plan.Id;
    }
}
