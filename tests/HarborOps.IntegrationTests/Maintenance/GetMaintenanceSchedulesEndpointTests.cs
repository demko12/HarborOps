using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class GetMaintenanceSchedulesEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetMaintenanceSchedulesEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_200_with_the_derived_vessel_and_due_state()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var equipment = await SeedEquipmentAsync(vesselId, "Main engine");
        var scheduleId = await SeedScheduleAsync(
            "Quarterly inspection",
            MaintenanceFrequency.Quarterly,
            new DateOnly(2020, 1, 1),
            null,
            equipment.Id);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/schedules", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content
            .ReadFromJsonAsync<GetMaintenanceSchedulesResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(GetMaintenanceSchedulesValidator.DefaultPageSize, page.PageSize);
        Assert.Equal(1, page.TotalCount);

        var item = Assert.Single(page.Items);
        Assert.Equal(scheduleId, item.ScheduleId);
        Assert.Equal(vesselId, item.VesselId);
        Assert.Equal("Quarterly", item.Frequency);
        Assert.Equal(1, item.EquipmentCount);
        Assert.True(item.IsOverdue);
        Assert.NotNull(item.DaysUntilDue);
    }

    [Fact]
    public async Task Get_filters_by_vessel_and_equipment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var mine = await SeedEquipmentAsync(vesselId, "Main engine");
        var other = await SeedEquipmentAsync(Guid.NewGuid(), "Boiler");

        await SeedScheduleAsync("Mine", MaintenanceFrequency.Monthly, new DateOnly(2026, 9, 1), null, mine.Id);
        await SeedScheduleAsync("Other", MaintenanceFrequency.Monthly, new DateOnly(2026, 9, 1), null, other.Id);

        using var byVessel = await client.GetAsync(
            new Uri($"/api/maintenance/schedules?vesselId={vesselId}", UriKind.Relative),
            CancellationToken.None);
        var byVesselPage = await byVessel.Content
            .ReadFromJsonAsync<GetMaintenanceSchedulesResponse>(CancellationToken.None);

        using var byEquipment = await client.GetAsync(
            new Uri($"/api/maintenance/schedules?equipmentId={other.Id}", UriKind.Relative),
            CancellationToken.None);
        var byEquipmentPage = await byEquipment.Content
            .ReadFromJsonAsync<GetMaintenanceSchedulesResponse>(CancellationToken.None);

        Assert.NotNull(byVesselPage);
        Assert.NotNull(byEquipmentPage);
        Assert.Equal("Mine", Assert.Single(byVesselPage.Items).MaintenanceType);
        Assert.Equal("Other", Assert.Single(byEquipmentPage.Items).MaintenanceType);
    }

    [Fact]
    public async Task Get_returns_400_for_an_inverted_due_window()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/schedules?dueFrom=2026-10-01&dueTo=2026-09-01", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetMaintenanceSchedulesValidator.DueToField, problem.Errors);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/schedules", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientAsync(roleNames: []);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/schedules", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Equipment> SeedEquipmentAsync(Guid vesselId, string name)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var equipment = Equipment.Register(vesselId, name, "Propulsion", null, null);

        dbContext.Set<Equipment>().Add(equipment);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return equipment;
    }

    private async Task<Guid> SeedScheduleAsync(
        string maintenanceType,
        MaintenanceFrequency frequency,
        DateOnly? nextDueDate,
        int? nextDueHours,
        params Guid[] equipmentIds)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var schedule = MaintenanceSchedule.Create(maintenanceType, frequency, nextDueDate, nextDueHours);

        dbContext.Set<MaintenanceSchedule>().Add(schedule);

        foreach (var equipmentId in equipmentIds)
        {
            dbContext.Set<ScheduleEquipment>().Add(ScheduleEquipment.Create(schedule.Id, equipmentId));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return schedule.Id;
    }
}
