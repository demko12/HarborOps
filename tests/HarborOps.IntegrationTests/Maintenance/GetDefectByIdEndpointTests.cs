using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class GetDefectByIdEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetDefectByIdEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_200_with_the_correct_fields_for_a_triaged_defect()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var defect = await SeedDefectAsync(vesselId, defectToSeed =>
        {
            defectToSeed.Triage(DefectSeverity.Critical, WorkOrderPriority.Critical, "Escalated on inspection", actorUserId, DateTime.UtcNow);
        });

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/defects/{defect.Id}", UriKind.Relative),
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<DefectDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(defect.Id, details!.Id);
        Assert.Equal(vesselId, details.VesselId);
        Assert.Equal("Triaged", details.Status);
        Assert.Equal("Medium", details.ReportedSeverity);
        Assert.Equal("Critical", details.ConfirmedSeverity);
        Assert.Equal("Critical", details.Priority);
        Assert.Equal("Escalated on inspection", details.TriageNotes);
        Assert.Equal(actorUserId, details.TriagedByUserId);
    }

    [Fact]
    public async Task Get_returns_403_without_the_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var defect = await SeedDefectAsync(Guid.NewGuid());

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/defects/{defect.Id}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_id()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/defects/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var defect = await SeedDefectAsync(Guid.NewGuid());

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/defects/{defect.Id}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<VesselDefect> SeedDefectAsync(Guid vesselId, Action<VesselDefect>? mutate = null)
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

        mutate?.Invoke(defect);

        await dbContext.Set<VesselDefect>().AddAsync(defect);
        await dbContext.SaveChangesAsync();

        return defect;
    }
}
