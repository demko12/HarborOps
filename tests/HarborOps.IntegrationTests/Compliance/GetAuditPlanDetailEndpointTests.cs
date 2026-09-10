using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class GetAuditPlanDetailEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public GetAuditPlanDetailEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_the_plan_with_its_history()
    {
        var planId = await SeedPlanAsync();

        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);
        using var response = await client.GetAsync($"/api/compliance/audit-plans/{planId}");
        var result = await response.Content.ReadFromJsonAsync<GetAuditPlanDetailResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(planId, result.Id);
        Assert.Empty(result.History);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_plan()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);

        using var response = await client.GetAsync($"/api/compliance/audit-plans/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_401_without_authentication()
    {
        var planId = await SeedPlanAsync();
        using var client = _application.CreateClient();

        using var response = await client.GetAsync($"/api/compliance/audit-plans/{planId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Guid> SeedPlanAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var plan = AuditPlan.Create(
            Guid.NewGuid(), SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10), null, "Scope.", null);

        await dbContext.AddAsync(plan);
        await dbContext.SaveChangesAsync();

        return plan.Id;
    }
}
