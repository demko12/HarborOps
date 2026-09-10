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

public sealed class CompleteAuditPlanEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public CompleteAuditPlanEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_completes_the_plan_and_creates_a_linked_survey_audit()
    {
        var planId = await SeedInProgressPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/audit-plans/{planId}/complete", ValidRequest());
        var result = await response.Content.ReadFromJsonAsync<CompleteAuditPlanResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var saved = await dbContext.Set<AuditPlan>().AsNoTracking().SingleAsync(plan => plan.Id == planId);
        Assert.Equal(AuditPlan.CompletedStatus, saved.Status);
        Assert.Equal(result.SurveyAuditId, saved.SurveyAuditId);

        var survey = await dbContext.Set<SurveyAudit>().AsNoTracking().SingleAsync(s => s.Id == result.SurveyAuditId);
        Assert.Equal(saved.VesselId, survey.VesselId);
    }

    [Fact]
    public async Task Post_returns_400_for_an_unsupported_result()
    {
        var planId = await SeedInProgressPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var request = ValidRequest() with { Result = "Passed with comments" };

        using var response = await client.PostAsJsonAsync($"/api/compliance/audit-plans/{planId}/complete", request);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(CompleteAuditPlanValidator.ResultField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_404_for_an_unknown_plan()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync($"/api/compliance/audit-plans/{Guid.NewGuid()}/complete", ValidRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_409_when_the_plan_is_not_in_progress()
    {
        var planId = await SeedScheduledPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync($"/api/compliance/audit-plans/{planId}/complete", ValidRequest());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_409_for_a_repeated_completion()
    {
        var planId = await SeedInProgressPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        await client.PostAsJsonAsync($"/api/compliance/audit-plans/{planId}/complete", ValidRequest());

        using var response = await client.PostAsJsonAsync($"/api/compliance/audit-plans/{planId}/complete", ValidRequest());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_403_with_read_only_permission()
    {
        var planId = await SeedInProgressPlanAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);

        using var response = await client.PostAsJsonAsync($"/api/compliance/audit-plans/{planId}/complete", ValidRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static CompleteAuditPlanRequest ValidRequest() => new()
    {
        Result = SurveyAudit.ResultSatisfactory,
        Findings = "All requirements met.",
        DocumentId = null,
        ActualAuditDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

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
