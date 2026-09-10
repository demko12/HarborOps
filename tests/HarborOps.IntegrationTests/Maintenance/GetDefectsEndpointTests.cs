using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class GetDefectsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetDefectsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_200_with_every_defect_when_no_filters_are_supplied()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        await SeedDefectAsync(vesselId, DefectStatus.Reported);
        await SeedDefectAsync(vesselId, DefectStatus.Triaged);

        using var response = await client.GetAsync(new Uri("/api/maintenance/defects", UriKind.Relative), CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetDefectsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task Get_filters_by_status()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        await SeedDefectAsync(vesselId, DefectStatus.Reported);
        var triaged = await SeedDefectAsync(vesselId, DefectStatus.Triaged);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/defects?status=Triaged", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetDefectsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Equal(triaged.Id, item.Id);
        Assert.Equal("Triaged", item.Status);
    }

    [Fact]
    public async Task Get_filters_by_vessel_id()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var targetVesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();
        var target = await SeedDefectAsync(targetVesselId, DefectStatus.Reported);
        await SeedDefectAsync(otherVesselId, DefectStatus.Reported);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/defects?vesselId={targetVesselId}", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetDefectsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(target.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task Get_returns_400_for_an_invalid_page_size()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/defects?pageSize=0", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetDefectsValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_400_for_an_invalid_status_filter()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/defects?status=Unknown", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetDefectsValidator.StatusField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_403_without_the_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);

        using var response = await client.GetAsync(new Uri("/api/maintenance/defects", UriKind.Relative), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/maintenance/defects", UriKind.Relative), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

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
}
