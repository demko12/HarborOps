using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class DefectTransitionEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public DefectTransitionEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    // ---- Triage ----

    [Fact]
    public async Task Triage_returns_200_from_reported()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Reported);

        using var response = await client.PostAsJsonAsync(
            TriageUri(defect.Id),
            new TriageDefectRequest { ConfirmedSeverity = "High", Priority = "High", Notes = "Confirmed active leak" },
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<DefectDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal("Triaged", details!.Status);
        Assert.Equal("High", details.ConfirmedSeverity);
        Assert.Equal("High", details.Priority);
        Assert.Equal(DefectStatus.Triaged, (await StoredDefectAsync(defect.Id)).Status);
    }

    [Fact]
    public async Task Triage_returns_400_for_an_invalid_severity()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Reported);

        using var response = await client.PostAsJsonAsync(
            TriageUri(defect.Id),
            new TriageDefectRequest { ConfirmedSeverity = "Severe", Priority = "High", Notes = null },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(TriageDefectValidator.ConfirmedSeverityField, problem.Errors);
    }

    [Fact]
    public async Task Triage_returns_403_without_the_manage_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Reported);

        using var response = await client.PostAsJsonAsync(
            TriageUri(defect.Id),
            new TriageDefectRequest { ConfirmedSeverity = "High", Priority = "High", Notes = null },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(DefectStatus.Reported, (await StoredDefectAsync(defect.Id)).Status);
    }

    [Fact]
    public async Task Triage_returns_404_for_an_unknown_defect()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);

        using var response = await client.PostAsJsonAsync(
            TriageUri(Guid.NewGuid()),
            new TriageDefectRequest { ConfirmedSeverity = "High", Priority = "High", Notes = null },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(DefectStatus.Triaged)]
    [InlineData(DefectStatus.Converted)]
    [InlineData(DefectStatus.Rejected)]
    public async Task Triage_returns_409_when_not_reported(DefectStatus status)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var defect = await SeedDefectAsync(Guid.NewGuid(), status);

        using var response = await client.PostAsJsonAsync(
            TriageUri(defect.Id),
            new TriageDefectRequest { ConfirmedSeverity = "High", Priority = "High", Notes = null },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(status, (await StoredDefectAsync(defect.Id)).Status);
    }

    // ---- Convert to work order ----

    [Fact]
    public async Task Convert_returns_201_from_triaged_and_the_new_work_order_is_gettable()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage,
            Permissions.Maintenance.Read);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Triaged);
        var dueUtc = DateTime.UtcNow.AddDays(10);

        using var response = await client.PostAsJsonAsync(
            ConvertUri(defect.Id),
            new ConvertDefectToWorkOrderRequest { DueUtc = dueUtc },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<ConvertDefectToWorkOrderResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(defect.Id, result!.DefectId);
        Assert.Equal(
            $"/api/maintenance/work-orders/{result.WorkOrderId}",
            response.Headers.Location?.OriginalString);

        using var workOrderResponse = await client.GetAsync(response.Headers.Location, CancellationToken.None);
        var workOrder = await workOrderResponse.Content.ReadFromJsonAsync<GetMaintenanceWorkOrderDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, workOrderResponse.StatusCode);
        Assert.NotNull(workOrder);
        Assert.Equal(result.WorkOrderId, workOrder!.WorkOrderId);
        Assert.Equal(defect.VesselId, workOrder.VesselId);
        Assert.Equal("Open", workOrder.Status);
        Assert.Equal("High", workOrder.Priority);
        Assert.Equal(dueUtc, workOrder.DueUtc);

        var storedDefect = await StoredDefectAsync(defect.Id);
        Assert.Equal(DefectStatus.Converted, storedDefect.Status);
        Assert.Equal(result.WorkOrderId, storedDefect.WorkOrderId);
    }

    [Fact]
    public async Task Convert_returns_400_for_a_missing_due_utc()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Triaged);

        using var response = await client.PostAsJsonAsync(
            ConvertUri(defect.Id),
            new ConvertDefectToWorkOrderRequest { DueUtc = null },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(ConvertDefectToWorkOrderValidator.DueUtcField, problem.Errors);
    }

    [Fact]
    public async Task Convert_returns_403_without_the_manage_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Triaged);

        using var response = await client.PostAsJsonAsync(
            ConvertUri(defect.Id),
            new ConvertDefectToWorkOrderRequest { DueUtc = DateTime.UtcNow.AddDays(5) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(DefectStatus.Triaged, (await StoredDefectAsync(defect.Id)).Status);
    }

    [Fact]
    public async Task Convert_returns_404_for_an_unknown_defect()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);

        using var response = await client.PostAsJsonAsync(
            ConvertUri(Guid.NewGuid()),
            new ConvertDefectToWorkOrderRequest { DueUtc = DateTime.UtcNow.AddDays(5) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Convert_returns_409_when_not_yet_triaged()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Reported);

        using var response = await client.PostAsJsonAsync(
            ConvertUri(defect.Id),
            new ConvertDefectToWorkOrderRequest { DueUtc = DateTime.UtcNow.AddDays(5) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Convert_returns_409_when_already_converted()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Converted);

        using var response = await client.PostAsJsonAsync(
            ConvertUri(defect.Id),
            new ConvertDefectToWorkOrderRequest { DueUtc = DateTime.UtcNow.AddDays(5) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- Reject ----

    [Fact]
    public async Task Reject_returns_200_from_reported()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Reported);

        using var response = await client.PostAsJsonAsync(
            RejectUri(defect.Id),
            new RejectDefectRequest { Reason = "Duplicate report" },
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<DefectDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal("Rejected", details!.Status);
        Assert.Equal("Duplicate report", details.RejectionReason);
    }

    [Fact]
    public async Task Reject_returns_200_from_triaged()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Triaged);

        using var response = await client.PostAsJsonAsync(
            RejectUri(defect.Id),
            new RejectDefectRequest { Reason = "Assessed as low priority" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DefectStatus.Rejected, (await StoredDefectAsync(defect.Id)).Status);
    }

    [Fact]
    public async Task Reject_returns_400_for_a_missing_reason()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Reported);

        using var response = await client.PostAsJsonAsync(
            RejectUri(defect.Id),
            new RejectDefectRequest { Reason = null },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RejectDefectValidator.ReasonField, problem.Errors);
    }

    [Fact]
    public async Task Reject_returns_403_without_the_manage_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Reported);

        using var response = await client.PostAsJsonAsync(
            RejectUri(defect.Id),
            new RejectDefectRequest { Reason = "Duplicate report" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(DefectStatus.Reported, (await StoredDefectAsync(defect.Id)).Status);
    }

    [Fact]
    public async Task Reject_returns_404_for_an_unknown_defect()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);

        using var response = await client.PostAsJsonAsync(
            RejectUri(Guid.NewGuid()),
            new RejectDefectRequest { Reason = "Duplicate report" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(DefectStatus.Converted)]
    [InlineData(DefectStatus.Rejected)]
    public async Task Reject_returns_409_when_already_settled(DefectStatus status)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var defect = await SeedDefectAsync(Guid.NewGuid(), status);

        using var response = await client.PostAsJsonAsync(
            RejectUri(defect.Id),
            new RejectDefectRequest { Reason = "Too late" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(status, (await StoredDefectAsync(defect.Id)).Status);
    }

    // ---- Full lifecycle, real HTTP calls in sequence ----

    [Fact]
    public async Task Full_lifecycle_register_then_triage_then_convert_succeeds_end_to_end()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage,
            Permissions.Maintenance.Read);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);

        using var registerResponse = await client.PostAsJsonAsync(
            "/api/maintenance/defects",
            new RegisterDefectRequest
            {
                VesselId = vessel.Id,
                EquipmentId = null,
                Title = "Hydraulic leak on crane boom",
                Description = "Visible oil leak near the boom cylinder",
                ReportedSeverity = "Medium",
                ObservedAtUtc = DateTime.UtcNow.AddHours(-1),
            },
            CancellationToken.None);
        var registered = await registerResponse.Content.ReadFromJsonAsync<DefectDetails>(CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        Assert.NotNull(registered);

        using var triageResponse = await client.PostAsJsonAsync(
            TriageUri(registered!.Id),
            new TriageDefectRequest { ConfirmedSeverity = "Critical", Priority = "Critical", Notes = "Escalated" },
            CancellationToken.None);
        var triaged = await triageResponse.Content.ReadFromJsonAsync<DefectDetails>(CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, triageResponse.StatusCode);
        Assert.Equal("Triaged", triaged!.Status);

        var dueUtc = DateTime.UtcNow.AddDays(3);
        using var convertResponse = await client.PostAsJsonAsync(
            ConvertUri(registered.Id),
            new ConvertDefectToWorkOrderRequest { DueUtc = dueUtc },
            CancellationToken.None);
        var converted = await convertResponse.Content.ReadFromJsonAsync<ConvertDefectToWorkOrderResponse>(
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, convertResponse.StatusCode);
        Assert.NotNull(converted);

        using var finalDefectResponse = await client.GetAsync(
            new Uri($"/api/maintenance/defects/{registered.Id}", UriKind.Relative),
            CancellationToken.None);
        var finalDefect = await finalDefectResponse.Content.ReadFromJsonAsync<DefectDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, finalDefectResponse.StatusCode);
        Assert.Equal("Converted", finalDefect!.Status);
        Assert.Equal(converted!.WorkOrderId, finalDefect.WorkOrderId);

        using var workOrderResponse = await client.GetAsync(
            new Uri($"/api/maintenance/work-orders/{converted.WorkOrderId}", UriKind.Relative),
            CancellationToken.None);
        var workOrder = await workOrderResponse.Content.ReadFromJsonAsync<GetMaintenanceWorkOrderDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, workOrderResponse.StatusCode);
        Assert.Equal(vessel.Id, workOrder!.VesselId);
        Assert.Equal("Critical", workOrder.Priority);
        Assert.Equal(dueUtc, workOrder.DueUtc);
    }

    // ---- Permissions, explicitly (Step 3) ----

    [Fact]
    public async Task A_read_only_caller_can_read_but_is_forbidden_from_every_mutating_action()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var reportedDefect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Reported);
        var triagedDefect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Triaged);

        using var list = await client.GetAsync(new Uri("/api/maintenance/defects", UriKind.Relative), CancellationToken.None);
        using var details = await client.GetAsync(
            new Uri($"/api/maintenance/defects/{reportedDefect.Id}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);

        using var register = await client.PostAsJsonAsync(
            "/api/maintenance/defects",
            new RegisterDefectRequest
            {
                VesselId = Guid.NewGuid(),
                Title = "Test",
                ReportedSeverity = "Low",
                ObservedAtUtc = DateTime.UtcNow,
            },
            CancellationToken.None);
        using var triage = await client.PostAsJsonAsync(
            TriageUri(reportedDefect.Id),
            new TriageDefectRequest { ConfirmedSeverity = "High", Priority = "High", Notes = null },
            CancellationToken.None);
        using var reject = await client.PostAsJsonAsync(
            RejectUri(reportedDefect.Id),
            new RejectDefectRequest { Reason = "Not applicable" },
            CancellationToken.None);
        using var convert = await client.PostAsJsonAsync(
            ConvertUri(triagedDefect.Id),
            new ConvertDefectToWorkOrderRequest { DueUtc = DateTime.UtcNow.AddDays(5) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, triage.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reject.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, convert.StatusCode);
    }

    [Fact]
    public async Task A_caller_with_no_maintenance_permission_is_blocked_from_everything()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var defect = await SeedDefectAsync(Guid.NewGuid(), DefectStatus.Reported);

        using var list = await client.GetAsync(new Uri("/api/maintenance/defects", UriKind.Relative), CancellationToken.None);
        using var details = await client.GetAsync(
            new Uri($"/api/maintenance/defects/{defect.Id}", UriKind.Relative),
            CancellationToken.None);
        using var register = await client.PostAsJsonAsync(
            "/api/maintenance/defects",
            new RegisterDefectRequest { VesselId = Guid.NewGuid(), Title = "Test", ReportedSeverity = "Low", ObservedAtUtc = DateTime.UtcNow },
            CancellationToken.None);
        using var triage = await client.PostAsJsonAsync(
            TriageUri(defect.Id),
            new TriageDefectRequest { ConfirmedSeverity = "High", Priority = "High", Notes = null },
            CancellationToken.None);
        using var reject = await client.PostAsJsonAsync(
            RejectUri(defect.Id),
            new RejectDefectRequest { Reason = "Not applicable" },
            CancellationToken.None);
        using var convert = await client.PostAsJsonAsync(
            ConvertUri(defect.Id),
            new ConvertDefectToWorkOrderRequest { DueUtc = DateTime.UtcNow.AddDays(5) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, details.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, triage.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reject.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, convert.StatusCode);
    }

    private static Uri TriageUri(Guid defectId) => new($"/api/maintenance/defects/{defectId}/triage", UriKind.Relative);

    private static Uri RejectUri(Guid defectId) => new($"/api/maintenance/defects/{defectId}/reject", UriKind.Relative);

    private static Uri ConvertUri(Guid defectId) =>
        new($"/api/maintenance/defects/{defectId}/convert-to-work-order", UriKind.Relative);

    private async Task<VesselDefect> SeedDefectAsync(Guid vesselId, DefectStatus status)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var defect = VesselDefect.Register(
            vesselId,
            null,
            "Hydraulic leak on crane boom",
            "Visible oil leak near the boom cylinder",
            DefectSeverity.Medium,
            DateTime.UtcNow.AddHours(-2),
            Guid.NewGuid(),
            DateTime.UtcNow.AddHours(-1));

        if (status is DefectStatus.Triaged or DefectStatus.Converted)
        {
            defect.Triage(DefectSeverity.High, WorkOrderPriority.High, null, Guid.NewGuid(), DateTime.UtcNow);
        }

        if (status is DefectStatus.Converted)
        {
            defect.ConvertToWorkOrder(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        }

        if (status is DefectStatus.Rejected)
        {
            defect.Reject("Not applicable", Guid.NewGuid(), DateTime.UtcNow);
        }

        await dbContext.Set<VesselDefect>().AddAsync(defect);
        await dbContext.SaveChangesAsync();

        return defect;
    }

    private async Task<VesselDefect> StoredDefectAsync(Guid defectId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<VesselDefect>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == defectId, CancellationToken.None);
    }

    private async Task<Guid> SeedVesselTypeAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vesselType = VesselType.Register("Container Ship", "CNTR", "Container carrying vessel");

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.SaveChangesAsync();

        return vesselType.Id;
    }

    private async Task<Vessel> SeedVesselAsync(Guid vesselTypeId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vessel = Vessel.Register(Guid.NewGuid(), vesselTypeId, "North Star", "1000001", "Malta", "Active");

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }
}
