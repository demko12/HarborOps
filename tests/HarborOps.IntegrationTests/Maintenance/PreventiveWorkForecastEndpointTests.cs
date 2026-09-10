using System.Globalization;
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

public sealed class PreventiveWorkForecastEndpointTests : IntegrationTestBase
{
    private static readonly Uri GenerateUri = new("/api/maintenance/preventive-work/generate", UriKind.Relative);

    private readonly HarborOpsApiFactory _application;
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);

    public PreventiveWorkForecastEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_200_with_due_occurrences_and_their_equipment_scope()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var mainEngine = await SeedEquipmentAsync(vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(vesselId, "Boiler");
        var scheduleId = await SeedScheduleAsync(
            "Main engine overhaul",
            MaintenanceFrequency.Monthly,
            _today.AddDays(5),
            null,
            mainEngine.Id,
            boiler.Id);

        using var response = await client.GetAsync(ForecastUri(_today, _today.AddDays(30)), CancellationToken.None);
        var forecast = await response.Content.ReadFromJsonAsync<GetPreventiveWorkForecastResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(forecast);

        var item = Assert.Single(forecast.Items);

        Assert.Equal(scheduleId, item.ScheduleId);
        Assert.Equal(vesselId, item.VesselId);
        Assert.Equal("Main engine overhaul", item.MaintenanceType);
        Assert.Equal(_today.AddDays(5), item.DueDate);
        Assert.Equal(DueUtc(_today.AddDays(5)), item.DueUtc);
        Assert.Equal([boiler.Id, mainEngine.Id], item.EquipmentIds);
        Assert.Equal("Pending", item.State);
        Assert.Null(item.WorkOrderId);
        Assert.Equal(1, forecast.PendingCount);
    }

    [Fact]
    public async Task Get_is_allowed_for_the_manage_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);

        using var response = await client.GetAsync(ForecastUri(_today, _today.AddDays(30)), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_links_generated_occurrences_and_filters_by_state()
    {
        using var manageClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");
        var generatedScheduleId = await SeedScheduleAsync(
            "Generated",
            MaintenanceFrequency.Monthly,
            _today.AddDays(2),
            null,
            equipment.Id);
        await SeedScheduleAsync("Pending", MaintenanceFrequency.Monthly, _today.AddDays(20), null, equipment.Id);
        var generated = Assert.Single((await GenerateAsync(manageClient, _today.AddDays(10))).Results);

        using var response = await readClient.GetAsync(
            ForecastUri(_today, _today.AddDays(30), "&state=generated"),
            CancellationToken.None);
        var forecast = await response.Content.ReadFromJsonAsync<GetPreventiveWorkForecastResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(forecast);

        var item = Assert.Single(forecast.Items);

        Assert.Equal(generatedScheduleId, item.ScheduleId);
        Assert.Equal("Generated", item.State);
        Assert.Equal(generated.OccurrenceId, item.OccurrenceId);
        Assert.Equal(generated.WorkOrderId, item.WorkOrderId);
        Assert.Equal(1, forecast.PendingCount);
        Assert.Equal(1, forecast.GeneratedCount);
    }

    [Fact]
    public async Task Get_filters_by_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var mine = await SeedEquipmentAsync(vesselId, "Main engine");
        var other = await SeedEquipmentAsync(Guid.NewGuid(), "Boiler");
        await SeedScheduleAsync("Mine", MaintenanceFrequency.Monthly, _today.AddDays(3), null, mine.Id);
        await SeedScheduleAsync("Other", MaintenanceFrequency.Monthly, _today.AddDays(3), null, other.Id);

        using var response = await client.GetAsync(
            ForecastUri(_today, _today.AddDays(30), $"&vesselId={vesselId}"),
            CancellationToken.None);
        var forecast = await response.Content.ReadFromJsonAsync<GetPreventiveWorkForecastResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(forecast);
        Assert.Equal("Mine", Assert.Single(forecast.Items).MaintenanceType);
    }

    [Fact]
    public async Task Get_pages_deterministically()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");

        for (var day = 1; day <= 5; day++)
        {
            await SeedScheduleAsync("Schedule", MaintenanceFrequency.Monthly, _today.AddDays(day), null, equipment.Id);
        }

        var all = await ReadForecastAsync(client, "&pageSize=100");
        var page = await ReadForecastAsync(client, "&pageNumber=2&pageSize=2");
        var again = await ReadForecastAsync(client, "&pageNumber=2&pageSize=2");

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(
            all.Items.Skip(2).Take(2).Select(item => item.ScheduleId),
            page.Items.Select(item => item.ScheduleId));
        Assert.Equal(
            page.Items.Select(item => item.ScheduleId),
            again.Items.Select(item => item.ScheduleId));
    }

    [Theory]
    [InlineData(null, null, null, GetPreventiveWorkForecastValidator.ToField)]
    [InlineData(367, null, null, GetPreventiveWorkForecastValidator.ToField)]
    [InlineData(30, "Unknown", null, GetPreventiveWorkForecastValidator.StateField)]
    [InlineData(30, null, 101, GetPreventiveWorkForecastValidator.PageSizeField)]
    public async Task Get_returns_400_for_an_unbounded_or_invalid_query(
        int? toOffset,
        string? state,
        int? pageSize,
        string expectedField)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var query = $"?from={Iso(_today)}";

        if (toOffset is { } offset)
        {
            query += $"&to={Iso(_today.AddDays(offset))}";
        }

        if (state is not null)
        {
            query += $"&state={state}";
        }

        if (pageSize is { } size)
        {
            query += $"&pageSize={size.ToString(CultureInfo.InvariantCulture)}";
        }

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/preventive-work/forecast{query}", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(expectedField, problem.Errors);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(ForecastUri(_today, _today.AddDays(30)), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_with_an_unrelated_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(ForecastUri(_today, _today.AddDays(30)), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Uri ForecastUri(DateOnly from, DateOnly to, string? extra = null)
    {
        return new Uri(
            $"/api/maintenance/preventive-work/forecast?from={Iso(from)}&to={Iso(to)}{extra}",
            UriKind.Relative);
    }

    private static string Iso(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateTime DueUtc(DateOnly dueDate)
    {
        return dueDate.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);
    }

    private async Task<GetPreventiveWorkForecastResponse> ReadForecastAsync(HttpClient client, string extra)
    {
        using var response = await client.GetAsync(
            ForecastUri(_today, _today.AddDays(10), extra),
            CancellationToken.None);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<GetPreventiveWorkForecastResponse>(CancellationToken.None))!;
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
