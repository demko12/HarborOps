using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class GetAuditPlansEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public GetAuditPlansEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_filtered_and_paged_results()
    {
        var vesselId = Guid.NewGuid();
        var expected = await SeedPlanAsync(vesselId, SurveyAudit.TypeAnnualSurvey, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));
        await SeedPlanAsync(vesselId, SurveyAudit.TypeInternalAudit, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));

        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);
        using var response = await client.GetAsync($"/api/compliance/audit-plans?vesselId={vesselId}&type={SurveyAudit.TypeAnnualSurvey}");
        var result = await response.Content.ReadFromJsonAsync<GetAuditPlansResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(expected.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task Get_returns_400_for_an_invalid_page_size()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);

        using var response = await client.GetAsync("/api/compliance/audit-plans?pageSize=0");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Get_returns_401_without_authentication()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync("/api/compliance/audit-plans");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_403_without_a_compliance_permission()
    {
        using var client = await _application.CreateAuthenticatedClientAsync(roleNames: []);

        using var response = await client.GetAsync("/api/compliance/audit-plans");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<AuditPlan> SeedPlanAsync(Guid vesselId, string type, DateOnly plannedStartDate, DateOnly dueDate)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var plan = AuditPlan.Create(vesselId, type, plannedStartDate, dueDate, null, "Scope.", null);

        await dbContext.AddAsync(plan);
        await dbContext.SaveChangesAsync();

        return plan;
    }
}
