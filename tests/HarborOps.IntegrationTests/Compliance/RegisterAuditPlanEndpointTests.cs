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

public sealed class RegisterAuditPlanEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public RegisterAuditPlanEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_creates_and_persists_a_scheduled_plan()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var vesselId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync("/api/compliance/audit-plans", ValidRequest(vesselId));
        var result = await response.Content.ReadFromJsonAsync<RegisterAuditPlanResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var saved = await dbContext.Set<AuditPlan>().AsNoTracking().SingleAsync(plan => plan.Id == result.Id);

        Assert.Equal(vesselId, saved.VesselId);
        Assert.Equal(AuditPlan.ScheduledStatus, saved.Status);
    }

    [Fact]
    public async Task Post_returns_400_for_an_unsupported_type()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var request = ValidRequest(Guid.NewGuid()) with { Type = "Random check" };

        using var response = await client.PostAsJsonAsync("/api/compliance/audit-plans", request);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterAuditPlanValidator.TypeField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_409_for_overlapping_active_plans()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var vesselId = Guid.NewGuid();
        await client.PostAsJsonAsync("/api/compliance/audit-plans", ValidRequest(vesselId));

        using var response = await client.PostAsJsonAsync("/api/compliance/audit-plans", ValidRequest(vesselId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_401_without_authentication()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/compliance/audit-plans", ValidRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_403_with_read_only_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);

        using var response = await client.PostAsJsonAsync("/api/compliance/audit-plans", ValidRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static RegisterAuditPlanRequest ValidRequest(Guid vesselId) => new()
    {
        VesselId = vesselId,
        Type = SurveyAudit.TypeAnnualSurvey,
        PlannedStartDate = new DateOnly(2026, 10, 1),
        DueDate = new DateOnly(2026, 10, 15),
        AssignedInspectorUserId = null,
        Scope = "Full hull and machinery survey.",
        Notes = null,
    };
}
