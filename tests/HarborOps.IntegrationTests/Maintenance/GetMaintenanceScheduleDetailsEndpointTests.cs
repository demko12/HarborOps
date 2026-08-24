using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class GetMaintenanceScheduleDetailsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetMaintenanceScheduleDetailsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_200_with_the_linked_equipment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var mainEngine = await SeedEquipmentAsync(vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(vesselId, "Boiler");
        var scheduleId = await SeedScheduleAsync(
            "Annual survey",
            MaintenanceFrequency.Annual,
            new DateOnly(2026, 12, 1),
            250,
            mainEngine.Id,
            boiler.Id);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/schedules/{scheduleId}", UriKind.Relative),
            CancellationToken.None);
        var details = await response.Content
            .ReadFromJsonAsync<GetMaintenanceScheduleDetailsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(scheduleId, details.ScheduleId);
        Assert.Equal(vesselId, details.VesselId);
        Assert.Equal("Annual", details.Frequency);
        Assert.Equal(250, details.NextDueHours);
        Assert.Equal(["Boiler", "Main engine"], details.Equipment.Select(item => item.Name));
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_schedule()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/schedules/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/schedules/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientAsync(roleNames: []);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/schedules/{Guid.NewGuid()}", UriKind.Relative),
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
