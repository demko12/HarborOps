using System.Net;
using System.Text;
using HarborOps.Web.Api.Compliance;

namespace HarborOps.Web.Tests.Api;

public sealed class ComplianceApiClientTests
{
    [Fact]
    public async Task Client_uses_the_actual_overview_and_write_endpoints()
    {
        var requests = new List<(HttpMethod Method, string Path)>();
        using var http = CreateClient((request, _) =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            var body = request.Method == HttpMethod.Get
                ? EmptyOverview
                : request.RequestUri!.AbsolutePath.EndsWith("/certificates", StringComparison.Ordinal)
                    ? "{\"id\":\"30000000-0000-0000-0000-000000000003\"}"
                    : "{\"id\":1}";
            return Json(request.Method == HttpMethod.Get ? HttpStatusCode.OK : HttpStatusCode.Created, body);
        });
        var client = new ComplianceApiClient(http);
        var vesselId = Guid.NewGuid();

        await client.GetOverviewAsync(vesselId, CancellationToken.None);
        var certificate = await client.RegisterCertificateAsync(new(vesselId, "Safety", "1", "Authority", new(2026, 1, 1), new(2027, 1, 1)), CancellationToken.None);
        var inspection = await client.RegisterPscInspectionAsync(new(vesselId, Guid.NewGuid(), new(2026, 8, 1), "Authority", 0, false), CancellationToken.None);
        var nonConformity = await client.RegisterNonConformityAsync(new(1, null, Guid.NewGuid(), "Major", new(2026, 12, 1)), CancellationToken.None);
        var insurancePolicy = await client.RegisterInsurancePolicyAsync(
            new(vesselId, "Harbor Mutual", "HM-2026-001", "P&I", new(2027, 1, 1)),
            CancellationToken.None);
        await client.GetInsurancePoliciesAsync($"vesselId={vesselId}&pageNumber=1&pageSize=25", CancellationToken.None);

        Assert.Contains((HttpMethod.Get, $"/api/compliance/vessels/{vesselId}/overview"), requests);
        Assert.Contains((HttpMethod.Post, "/api/compliance/certificates"), requests);
        Assert.Contains((HttpMethod.Post, "/api/compliance/psc-inspections"), requests);
        Assert.Contains((HttpMethod.Post, "/api/compliance/non-conformities"), requests);
        Assert.Contains((HttpMethod.Post, "/api/compliance/insurance-policies"), requests);
        Assert.Contains((HttpMethod.Get, "/api/compliance/insurance-policies"), requests);
        Assert.Equal(Guid.Parse("30000000-0000-0000-0000-000000000003"), certificate.Value!.Id);
        Assert.Equal(1, inspection.Value!.Id);
        Assert.Equal(1, nonConformity.Value!.Id);
        Assert.Equal(1, insurancePolicy.Value!.Id);
    }

    [Fact]
    public async Task Client_maps_server_validation_errors()
    {
        using var http = CreateClient((_, _) => Json(HttpStatusCode.BadRequest,
            "{\"title\":\"Validation failed\",\"errors\":{\"certificateNumber\":[\"Required\"]}}"));
        var result = await new ComplianceApiClient(http).RegisterCertificateAsync(
            new(Guid.NewGuid(), null, null, null, null, null), CancellationToken.None);
        Assert.True(result.Failure!.IsValidation);
        Assert.Equal("Required", Assert.Single(result.Failure.Errors["certificateNumber"]));
    }

    [Fact]
    public async Task Client_uses_the_actual_audit_plan_endpoints()
    {
        var requests = new List<(HttpMethod Method, string Path)>();
        var planId = Guid.NewGuid();
        using var http = CreateClient((request, _) =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            var body = request.RequestUri!.AbsolutePath.Contains("/complete", StringComparison.Ordinal)
                ? $"{{\"id\":\"{planId}\",\"surveyAuditId\":1,\"completedAtUtc\":\"2026-09-15T10:00:00Z\"}}"
                : request.RequestUri!.AbsolutePath.EndsWith("/start", StringComparison.Ordinal)
                    ? $"{{\"id\":\"{planId}\",\"status\":\"InProgress\",\"startedAtUtc\":\"2026-09-15T10:00:00Z\"}}"
                    : request.RequestUri!.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal)
                        ? $"{{\"id\":\"{planId}\",\"status\":\"Cancelled\",\"cancelledAtUtc\":\"2026-09-15T10:00:00Z\"}}"
                        : request.RequestUri!.AbsolutePath.EndsWith("/schedule", StringComparison.Ordinal)
                            ? $"{{\"id\":\"{planId}\",\"plannedStartDate\":\"2026-10-01\",\"dueDate\":\"2026-10-15\"}}"
                            : request.RequestUri!.AbsolutePath.EndsWith("/assignment", StringComparison.Ordinal)
                                ? $"{{\"id\":\"{planId}\",\"inspectorUserId\":\"{Guid.NewGuid()}\"}}"
                                : request.Method == HttpMethod.Post
                                    ? $"{{\"id\":\"{planId}\"}}"
                                    : request.RequestUri!.AbsolutePath.EndsWith($"/{planId}", StringComparison.Ordinal)
                                        ? $"{{\"id\":\"{planId}\",\"vesselId\":\"{Guid.NewGuid()}\",\"type\":\"Annual survey\",\"status\":\"Scheduled\",\"plannedStartDate\":\"2026-10-01\",\"dueDate\":\"2026-10-15\",\"assignedInspectorUserId\":null,\"scope\":\"Full survey.\",\"notes\":null,\"isOverdue\":false,\"surveyAuditId\":null,\"history\":[]}}"
                                        : "{\"items\":[],\"totalCount\":0,\"pageNumber\":1,\"pageSize\":25}";

            return Json(request.Method == HttpMethod.Post
                ? request.RequestUri!.AbsolutePath.EndsWith("/audit-plans", StringComparison.Ordinal) ? HttpStatusCode.Created : HttpStatusCode.OK
                : HttpStatusCode.OK, body);
        });
        var client = new ComplianceApiClient(http);

        await client.GetAuditPlansAsync(new GetAuditPlansRequest(), CancellationToken.None);
        await client.GetAuditPlanDetailAsync(planId, CancellationToken.None);
        await client.RegisterAuditPlanAsync(
            new RegisterAuditPlanRequest { VesselId = Guid.NewGuid(), Type = "Annual survey", PlannedStartDate = new(2026, 10, 1), DueDate = new(2026, 10, 15), Scope = "Scope." },
            CancellationToken.None);
        await client.RescheduleAuditPlanAsync(
            planId, new RescheduleAuditPlanRequest { PlannedStartDate = new(2026, 10, 1), DueDate = new(2026, 10, 15), Reason = "Reason." }, CancellationToken.None);
        await client.AssignAuditPlanInspectorAsync(
            planId, new AssignAuditPlanInspectorRequest { InspectorUserId = Guid.NewGuid() }, CancellationToken.None);
        await client.StartAuditPlanAsync(planId, CancellationToken.None);
        await client.CompleteAuditPlanAsync(
            planId, new CompleteAuditPlanRequest { Result = "Satisfactory", Findings = "Fine.", ActualAuditDate = new(2026, 9, 15) }, CancellationToken.None);
        await client.CancelAuditPlanAsync(planId, new CancelAuditPlanRequest("Reason."), CancellationToken.None);

        Assert.Contains((HttpMethod.Get, "/api/compliance/audit-plans"), requests);
        Assert.Contains((HttpMethod.Get, $"/api/compliance/audit-plans/{planId}"), requests);
        Assert.Contains((HttpMethod.Post, "/api/compliance/audit-plans"), requests);
        Assert.Contains((HttpMethod.Put, $"/api/compliance/audit-plans/{planId}/schedule"), requests);
        Assert.Contains((HttpMethod.Put, $"/api/compliance/audit-plans/{planId}/assignment"), requests);
        Assert.Contains((HttpMethod.Post, $"/api/compliance/audit-plans/{planId}/start"), requests);
        Assert.Contains((HttpMethod.Post, $"/api/compliance/audit-plans/{planId}/complete"), requests);
        Assert.Contains((HttpMethod.Post, $"/api/compliance/audit-plans/{planId}/cancel"), requests);
    }

    [Fact]
    public async Task Client_maps_audit_plan_conflict_and_not_found_failures()
    {
        using var conflictHttp = CreateClient((_, _) => Json(HttpStatusCode.Conflict, "{\"title\":\"Conflict\",\"detail\":\"Overlaps another plan.\"}"));
        var conflictResult = await new ComplianceApiClient(conflictHttp).RegisterAuditPlanAsync(
            new RegisterAuditPlanRequest { VesselId = Guid.NewGuid(), Type = "Annual survey", PlannedStartDate = new(2026, 10, 1), DueDate = new(2026, 10, 15), Scope = "Scope." },
            CancellationToken.None);
        Assert.True(conflictResult.Failure!.IsConflict);

        using var notFoundHttp = CreateClient((_, _) => Json(HttpStatusCode.NotFound, "{\"title\":\"Not found\"}"));
        var notFoundResult = await new ComplianceApiClient(notFoundHttp).GetAuditPlanDetailAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.True(notFoundResult.Failure!.IsNotFound);
    }

    private const string EmptyOverview = "{\"certificates\":[],\"pscInspections\":[],\"openNonConformities\":[]}";
    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
        new(new Handler(handler)) { BaseAddress = new Uri("https://localhost/") };
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(handler(request, token)); }
}
