using System.Net;
using System.Net.Http.Json;
using HarborOps.Api.Composition.Reports.MaintenanceBacklog;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Reports;

public sealed class MaintenanceBacklogReportEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public MaintenanceBacklogReportEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_composed_page_with_overdue_flag()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        var asOfUtc = asOf.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var overdue = await SeedWorkOrderAsync(vesselId, "Overdue order", WorkOrderPriority.Critical, asOfUtc.AddDays(-1));
        await SeedWorkOrderAsync(vesselId, "Future order", WorkOrderPriority.Low, asOfUtc.AddDays(1));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/maintenance-backlog?asOf={asOf:yyyy-MM-dd}&overdue=true",
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<MaintenanceBacklogReportPage>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(asOf, page.AsOf);
        var item = Assert.Single(page.Items);
        Assert.Equal(overdue.Id, item.WorkOrderId);
        Assert.True(item.IsOverdue);
        Assert.Equal("Critical", item.Priority);
    }

    [Fact]
    public async Task Get_filters_by_priority_and_status()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        var asOfUtc = asOf.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var critical = await SeedWorkOrderAsync(vesselId, "Critical order", WorkOrderPriority.Critical, asOfUtc.AddDays(1));
        await SeedWorkOrderAsync(vesselId, "Low order", WorkOrderPriority.Low, asOfUtc.AddDays(1));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/maintenance-backlog?asOf={asOf:yyyy-MM-dd}&priority=Critical&status=Open",
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<MaintenanceBacklogReportPage>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        var item = Assert.Single(page.Items);
        Assert.Equal(critical.Id, item.WorkOrderId);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_an_unrecognized_priority()
    {
        var asOf = new DateOnly(2026, 8, 15);
        using var client = await CreateReportClientAsync();

        using var response = await client.GetAsync(
            $"/api/reports/maintenance-backlog?asOf={asOf:yyyy-MM-dd}&priority=Urgent",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(MaintenanceBacklogReportValidator.PriorityField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_validation_problem_when_as_of_is_missing()
    {
        using var client = await CreateReportClientAsync();

        using var response = await client.GetAsync("/api/reports/maintenance-backlog", CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(MaintenanceBacklogReportValidator.AsOfField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_unauthorized_for_an_anonymous_request()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            "/api/reports/maintenance-backlog?asOf=2026-08-15",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_the_reports_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            "/api/reports/maintenance-backlog?asOf=2026-08-15",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_the_maintenance_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Reports.Read);

        using var response = await client.GetAsync(
            "/api/reports/maintenance-backlog?asOf=2026-08-15",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_returns_csv_with_headers_matching_json_rows()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        var asOfUtc = asOf.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var seeded = await SeedWorkOrderAsync(vesselId, "Replace impeller", WorkOrderPriority.High, asOfUtc.AddDays(3));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/maintenance-backlog/export?asOf={asOf:yyyy-MM-dd}",
            CancellationToken.None);
        var csv = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(
            $"maintenance-backlog-{asOf:yyyy-MM-dd}.csv",
            response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        var lines = csv.Replace("﻿", string.Empty).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            "WorkOrderId,VesselId,Title,Priority,Status,DueUtc,IsOverdue,AssignedEngineerId,CreatedUtc,CompletedUtc",
            lines[0]);
        Assert.Contains(seeded.Id.ToString(), lines[1]);
        Assert.Contains("Replace impeller", lines[1]);
    }

    [Fact]
    public async Task Export_guards_against_formula_injection_in_the_title()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        var asOfUtc = asOf.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await SeedWorkOrderAsync(vesselId, "=cmd|'/c calc'!A1", WorkOrderPriority.Medium, asOfUtc.AddDays(3));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/maintenance-backlog/export?asOf={asOf:yyyy-MM-dd}",
            CancellationToken.None);
        var csv = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Contains("'=cmd|'/c calc'!A1", csv);
    }

    [Fact]
    public async Task Export_returns_validation_problem_for_an_unrecognized_status()
    {
        var asOf = new DateOnly(2026, 8, 15);
        using var client = await CreateReportClientAsync();

        using var response = await client.GetAsync(
            $"/api/reports/maintenance-backlog/export?asOf={asOf:yyyy-MM-dd}&status=Blocked",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(MaintenanceBacklogReportValidator.StatusField, problem.Errors);
    }

    private Task<HttpClient> CreateReportClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Reports.Read,
            Permissions.Maintenance.Read);
    }

    private async Task<MaintenanceWorkOrder> SeedWorkOrderAsync(
        Guid vesselId, string title, WorkOrderPriority priority, DateTime dueUtc)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var workOrder = MaintenanceWorkOrder.Create(vesselId, title, null, priority, dueUtc);
        dbContext.Add(workOrder);
        await dbContext.SaveChangesAsync();

        return workOrder;
    }
}
