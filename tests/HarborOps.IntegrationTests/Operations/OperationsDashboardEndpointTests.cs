using System.Net;
using System.Net.Http.Json;
using HarborOps.Api.Composition.Operations.Dashboard;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Operations;

public sealed class OperationsDashboardEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory application;

    public OperationsDashboardEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        this.application = application;
    }

    [Fact]
    public async Task Get_requires_an_authenticated_user()
    {
        using var client = application.CreateClient();
        using var response = await client.GetAsync("/api/operations/dashboard?asOf=2026-08-17");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_requires_an_as_of_date()
    {
        using var client = await application.CreateAuthenticatedClientAsync();
        using var response = await client.GetAsync("/api/operations/dashboard");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_omits_sections_for_permissions_the_user_does_not_have()
    {
        using var client = await application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);
        using var response = await client.GetAsync("/api/operations/dashboard?asOf=2026-08-17");
        var dashboard = await response.Content.ReadFromJsonAsync<OperationsDashboardResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(dashboard);
        var section = Assert.Single(dashboard.Sections);
        Assert.Equal("fleet", section.Capability);
        Assert.InRange(section.Items.Count, 0, 10);
    }

    [Fact]
    public async Task Get_links_critical_and_overdue_work_orders_to_their_own_details()
    {
        var criticalWorkOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Inspect engine", null, WorkOrderPriority.Critical,
            new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
        var overdueWorkOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Inspect engine", null, WorkOrderPriority.High,
            new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc));
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
            dbContext.Set<MaintenanceWorkOrder>().AddRange(criticalWorkOrder, overdueWorkOrder);
            await dbContext.SaveChangesAsync();
        }

        using var client = await application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        using var response = await client.GetAsync("/api/operations/dashboard?asOf=2026-08-17");
        var dashboard = await response.Content.ReadFromJsonAsync<OperationsDashboardResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(dashboard);
        var section = Assert.Single(dashboard.Sections);
        Assert.Equal("maintenance", section.Capability);
        Assert.Equal("/maintenance?overdue=true", section.Link);
        Assert.Equal(2, section.Items.Count);
        Assert.Equal(2, dashboard.PriorityItems.Count);
        foreach (var workOrder in new[] { criticalWorkOrder, overdueWorkOrder })
        {
            var detailsLink = $"/maintenance/work-orders/{workOrder.Id}";
            Assert.Contains(section.Items, item => item.Link == detailsLink);
            Assert.Contains(dashboard.PriorityItems, item => item.Link == detailsLink);
        }
    }

    [Fact]
    public async Task Get_returns_an_empty_permission_aware_dashboard_when_no_module_read_permission_exists()
    {
        using var client = await application.CreateAuthenticatedClientAsync();
        using var response = await client.GetAsync("/api/operations/dashboard?asOf=2026-08-17");
        var dashboard = await response.Content.ReadFromJsonAsync<OperationsDashboardResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(dashboard);
        Assert.Empty(dashboard.Sections);
    }
}
