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

public sealed class RegisterMaintenanceScheduleEndpointTests : IntegrationTestBase
{
    private static readonly Uri SchedulesUri = new("/api/maintenance/schedules", UriKind.Relative);

    private readonly HarborOpsApiFactory _application;

    public RegisterMaintenanceScheduleEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_returns_201_and_links_the_equipment_atomically()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();
        var mainEngine = await SeedEquipmentAsync(vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(vesselId, "Boiler");

        using var response = await client.PostAsJsonAsync(
            SchedulesUri,
            new
            {
                maintenanceType = "Quarterly inspection",
                frequency = "Quarterly",
                nextDueDate = "2026-09-30",
                equipmentIds = new[] { mainEngine.Id, boiler.Id },
            },
            CancellationToken.None);

        var created = await response.Content
            .ReadFromJsonAsync<RegisterMaintenanceScheduleResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var schedule = await dbContext.Set<MaintenanceSchedule>().SingleAsync(CancellationToken.None);
        var links = await dbContext.Set<ScheduleEquipment>().ToListAsync(CancellationToken.None);

        Assert.Equal(created.Id, schedule.Id);
        Assert.Equal("Quarterly", schedule.Frequency);
        Assert.Equal(new DateOnly(2026, 9, 30), schedule.NextDueDate);
        Assert.Equal(2, links.Count);
    }

    [Fact]
    public async Task Post_returns_400_without_a_due_trigger()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");

        using var response = await client.PostAsJsonAsync(
            SchedulesUri,
            new
            {
                maintenanceType = "Inspection",
                frequency = "Monthly",
                equipmentIds = new[] { equipment.Id },
            },
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterMaintenanceScheduleValidator.NextDueDateField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_400_for_an_unsupported_frequency()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");

        using var response = await client.PostAsJsonAsync(
            SchedulesUri,
            new
            {
                maintenanceType = "Inspection",
                frequency = "Fortnightly",
                nextDueDate = "2026-09-30",
                equipmentIds = new[] { equipment.Id },
            },
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterMaintenanceScheduleValidator.FrequencyField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_404_for_equipment_maintenance_does_not_own()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);

        using var response = await client.PostAsJsonAsync(
            SchedulesUri,
            new
            {
                maintenanceType = "Inspection",
                frequency = "Monthly",
                nextDueDate = "2026-09-30",
                equipmentIds = new[] { Guid.NewGuid() },
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_409_when_the_equipment_spans_more_than_one_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var first = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");
        var second = await SeedEquipmentAsync(Guid.NewGuid(), "Boiler");

        using var response = await client.PostAsJsonAsync(
            SchedulesUri,
            new
            {
                maintenanceType = "Inspection",
                frequency = "Monthly",
                nextDueDate = "2026-09-30",
                equipmentIds = new[] { first.Id, second.Id },
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        Assert.Empty(await dbContext.Set<MaintenanceSchedule>().ToListAsync(CancellationToken.None));
        Assert.Empty(await dbContext.Set<ScheduleEquipment>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            SchedulesUri,
            new
            {
                maintenanceType = "Inspection",
                frequency = "Monthly",
                nextDueDate = "2026-09-30",
                equipmentIds = new[] { Guid.NewGuid() },
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_only_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.PostAsJsonAsync(
            SchedulesUri,
            new
            {
                maintenanceType = "Inspection",
                frequency = "Monthly",
                nextDueDate = "2026-09-30",
                equipmentIds = new[] { Guid.NewGuid() },
            },
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
}
