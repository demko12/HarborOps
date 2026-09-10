using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class ScheduleOccurrenceLinksEndpointTests : IntegrationTestBase
{
    private static readonly Uri GenerateUri = new("/api/maintenance/preventive-work/generate", UriKind.Relative);

    private readonly HarborOpsApiFactory _application;
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);

    public ScheduleOccurrenceLinksEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_occurrences_returns_200_with_the_work_order_links()
    {
        using var manageClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");
        var scheduleId = await SeedScheduleAsync(
            "Weekly check",
            MaintenanceFrequency.Weekly,
            _today.AddDays(1),
            null,
            equipment.Id);
        var generated = await GenerateAsync(manageClient, _today.AddDays(8));

        using var response = await readClient.GetAsync(OccurrencesUri(scheduleId), CancellationToken.None);
        var occurrences = await response.Content.ReadFromJsonAsync<GetScheduleOccurrencesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(occurrences);
        Assert.Equal(scheduleId, occurrences.ScheduleId);
        Assert.Equal("Weekly", occurrences.Frequency);
        Assert.Equal([_today.AddDays(1), _today.AddDays(8)], occurrences.Items.Select(item => item.DueDate));
        Assert.Equal(
            generated.Results.Select(result => result.WorkOrderId),
            occurrences.Items.Select(item => item.WorkOrderId));
        Assert.All(occurrences.Items, item => Assert.Equal("Generated", item.State));
        Assert.Equal(DueUtc(_today.AddDays(1)), occurrences.Items[0].DueUtc);
    }

    [Fact]
    public async Task Get_occurrences_returns_404_for_an_unknown_schedule()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(OccurrencesUri(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_occurrences_returns_409_for_an_hour_only_schedule()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var scheduleId = await SeedScheduleAsync("Bearing check", MaintenanceFrequency.RunningHours, null, 500);

        using var response = await client.GetAsync(OccurrencesUri(scheduleId), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Get_work_orders_returns_200_with_the_generated_work_orders()
    {
        using var manageClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var equipment = await SeedEquipmentAsync(vesselId, "Main engine");
        var scheduleId = await SeedScheduleAsync(
            "Overhaul",
            MaintenanceFrequency.Monthly,
            _today.AddDays(5),
            null,
            equipment.Id);
        var generated = Assert.Single((await GenerateAsync(manageClient, _today.AddDays(10))).Results);

        using var response = await readClient.GetAsync(WorkOrdersUri(scheduleId), CancellationToken.None);
        var workOrders = await response.Content.ReadFromJsonAsync<GetScheduleWorkOrdersResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(workOrders);

        var item = Assert.Single(workOrders.Items);

        Assert.Equal(generated.WorkOrderId, item.WorkOrderId);
        Assert.Equal(generated.OccurrenceId, item.OccurrenceId);
        Assert.Equal(vesselId, item.VesselId);
        Assert.Equal(_today.AddDays(5), item.OccurrenceDueDate);
        Assert.Equal("Open", item.Status);
        Assert.Equal("Generated", item.OccurrenceState);
    }

    [Fact]
    public async Task Get_work_orders_returns_404_for_an_unknown_schedule()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(WorkOrdersUri(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Uri OccurrencesUri(Guid scheduleId)
    {
        return new Uri($"/api/maintenance/schedules/{scheduleId}/occurrences", UriKind.Relative);
    }

    private static Uri WorkOrdersUri(Guid scheduleId)
    {
        return new Uri($"/api/maintenance/schedules/{scheduleId}/work-orders", UriKind.Relative);
    }

    private static DateTime DueUtc(DateOnly dueDate)
    {
        return dueDate.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);
    }

    private static async Task<GeneratePreventiveWorkResponse> GenerateAsync(HttpClient client, DateOnly through)
    {
        using var response = await client.PostAsJsonAsync(
            GenerateUri,
            new GeneratePreventiveWorkRequest { Through = through },
            CancellationToken.None);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<GeneratePreventiveWorkResponse>(CancellationToken.None))!;
    }

    private async Task<Equipment> SeedEquipmentAsync(Guid vesselId, string name)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var equipment = Equipment.Register(vesselId, name, "Propulsion", "MAN", null);

        await dbContext.Set<Equipment>().AddAsync(equipment, CancellationToken.None);
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

        await dbContext.Set<MaintenanceSchedule>().AddAsync(schedule, CancellationToken.None);

        foreach (var equipmentId in equipmentIds)
        {
            await dbContext.Set<ScheduleEquipment>().AddAsync(
                ScheduleEquipment.Create(schedule.Id, equipmentId),
                CancellationToken.None);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return schedule.Id;
    }
}
