using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class GetVesselMaintenanceOverviewEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;
    public GetVesselMaintenanceOverviewEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_returns_summaries_for_multiple_vessel_ids()
    {
        using var client = _application.CreateClient();
        var vesselIdWithWork = Guid.NewGuid();
        var vesselIdWithoutWork = Guid.NewGuid();

        await using (var scope = _application.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
            await dbContext.Set<MaintenanceWorkOrder>().AddAsync(MaintenanceWorkOrder.Create(
                vesselIdWithWork,
                "Replace filter",
                "Routine replacement",
                WorkOrderPriority.High,
                DateTime.UtcNow.AddDays(3)));

            await dbContext.SaveChangesAsync();
        }

        var request = new VesselMaintenanceOverviewRequest
        {
            VesselIds = [vesselIdWithWork, vesselIdWithoutWork],
        };

        using var response = await client.PostAsJsonAsync(
            "/api/maintenance/vessel-overviews/query",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VesselMaintenanceOverviewResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(2, result.Summaries.Count);

        var summaryWithWork = result.Summaries.Single(summary => summary.VesselId == vesselIdWithWork);
        var summaryWithoutWork = result.Summaries.Single(summary => summary.VesselId == vesselIdWithoutWork);

        Assert.Equal(1, summaryWithWork.OpenWorkOrderCount);
        Assert.Equal(0, summaryWithoutWork.OpenWorkOrderCount);
        Assert.Null(summaryWithoutWork.NextWorkOrderDueUtc);

    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = _application.CreateClient();
        var request = new VesselMaintenanceOverviewRequest
        {
            VesselIds = [Guid.Empty],
        };

        using var response = await client.PostAsJsonAsync(
            "/api/maintenance/vessel-overviews/query",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetVesselMaintenanceOverviewValidator.VesselIdsField, problem.Errors);
    }

    [Fact]
    public async Task Post_response_exposes_dto_shape_not_entity_fields()
    {
        using var client = _application.CreateClient();
        var vesselId = Guid.NewGuid();
        var request = new VesselMaintenanceOverviewRequest
        {
            VesselIds = [vesselId],
        };

        using var response = await client.PostAsJsonAsync(
            "/api/maintenance/vessel-overviews/query",
            request,
            CancellationToken.None);
        var payload = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("openWorkOrderCount", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overdueWorkOrderCount", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("criticalOpenWorkOrderCount", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nextWorkOrderDueUtc", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dryDockId", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("assignedEngineerId", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("title", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("description", payload, StringComparison.OrdinalIgnoreCase);
    }
}
