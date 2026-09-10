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

public sealed class PreventiveWorkGenerationEndpointTests : IntegrationTestBase
{
    private static readonly Uri GenerateUri = new("/api/maintenance/preventive-work/generate", UriKind.Relative);
    private static readonly Uri PreviewUri = new("/api/maintenance/preventive-work/generate/preview", UriKind.Relative);

    private readonly HarborOpsApiFactory _application;
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);

    public PreventiveWorkGenerationEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Preview_returns_200_and_persists_nothing()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync("Overhaul", MaintenanceFrequency.Monthly, _today.AddDays(5), null, equipment.Id);

        using var response = await client.PostAsJsonAsync(
            PreviewUri,
            new GeneratePreventiveWorkRequest { Through = _today.AddDays(10) },
            CancellationToken.None);
        var preview = await response.Content.ReadFromJsonAsync<PreviewPreventiveWorkResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(preview);
        Assert.Equal(1, preview.EligibleCount);
        Assert.Equal(0, preview.SkippedCount);
        Assert.Equal("Pending", Assert.Single(preview.Items).State);
        Assert.False(await QueryAsync(db => db.Set<MaintenanceWorkOrder>().AnyAsync()));
        Assert.False(await QueryAsync(db => db.Set<ScheduleOccurrence>().AnyAsync()));
    }

    [Fact]
    public async Task Preview_with_only_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.PostAsJsonAsync(
            PreviewUri,
            new GeneratePreventiveWorkRequest { Through = _today.AddDays(10) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Generate_returns_201_and_creates_a_linked_work_order_with_equipment_scope()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
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

        using var response = await client.PostAsJsonAsync(
            GenerateUri,
            new GeneratePreventiveWorkRequest { Through = _today.AddDays(10) },
            CancellationToken.None);
        var generated = await response.Content.ReadFromJsonAsync<GeneratePreventiveWorkResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/maintenance/preventive-work/forecast", response.Headers.Location?.OriginalString);
        Assert.NotNull(generated);
        Assert.Equal(1, generated.CreatedCount);

        var result = Assert.Single(generated.Results);
        var workOrder = await QueryAsync(db => db.Set<MaintenanceWorkOrder>().AsNoTracking().SingleAsync());
        var occurrence = await QueryAsync(db => db.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync());
        var scope = await QueryAsync(db => db.Set<WorkOrderEquipment>()
            .AsNoTracking()
            .Where(link => link.WorkOrderId == workOrder.Id)
            .Select(link => link.EquipmentId)
            .ToListAsync());

        Assert.Equal("Created", result.Outcome);
        Assert.Equal(workOrder.Id, result.WorkOrderId);
        Assert.Equal(occurrence.Id, result.OccurrenceId);
        Assert.Equal(vesselId, workOrder.VesselId);
        Assert.Equal(scheduleId, workOrder.SourceScheduleId);
        Assert.Equal(_today.AddDays(5), workOrder.SourceOccurrenceDueDate);
        Assert.Equal(DueUtc(_today.AddDays(5)), workOrder.DueUtc);
        Assert.Equal(WorkOrderStatus.Open, workOrder.Status);
        Assert.Equal(WorkOrderPriority.Medium, workOrder.Priority);
        Assert.Equal(new[] { mainEngine.Id, boiler.Id }.Order(), scope.Order());
        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.Equal(workOrder.Id, occurrence.WorkOrderId);
    }

    [Fact]
    public async Task Generate_returns_the_existing_link_for_a_repeated_request()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync("Overhaul", MaintenanceFrequency.Monthly, _today.AddDays(5), null, equipment.Id);
        var first = Assert.Single((await GenerateAsync(client, _today.AddDays(10))).Results);

        using var response = await client.PostAsJsonAsync(
            GenerateUri,
            new GeneratePreventiveWorkRequest { Through = _today.AddDays(10) },
            CancellationToken.None);
        var repeated = await response.Content.ReadFromJsonAsync<GeneratePreventiveWorkResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(repeated);
        Assert.Equal(0, repeated.CreatedCount);
        Assert.Equal(1, repeated.SkippedCount);

        var skipped = Assert.Single(repeated.Results);

        Assert.Equal("Skipped", skipped.Outcome);
        Assert.Equal(first.WorkOrderId, skipped.WorkOrderId);
        Assert.Single(await QueryAsync(db => db.Set<MaintenanceWorkOrder>().AsNoTracking().ToListAsync()));
        Assert.Single(await QueryAsync(db => db.Set<ScheduleOccurrence>().AsNoTracking().ToListAsync()));
    }

    [Theory]
    [InlineData(null, null, GeneratePreventiveWorkValidator.ThroughField)]
    [InlineData(-1, null, GeneratePreventiveWorkValidator.ThroughField)]
    [InlineData(400, null, GeneratePreventiveWorkValidator.ThroughField)]
    [InlineData(5, "Urgent", GeneratePreventiveWorkValidator.PriorityField)]
    public async Task Generate_returns_400_for_an_invalid_request(
        int? throughOffset,
        string? priority,
        string expectedField)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync("Overhaul", MaintenanceFrequency.Monthly, _today.AddDays(2), null, equipment.Id);

        using var response = await client.PostAsJsonAsync(
            GenerateUri,
            new GeneratePreventiveWorkRequest
            {
                Through = throughOffset is { } offset ? _today.AddDays(offset) : null,
                Priority = priority,
            },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(expectedField, problem.Errors);
        Assert.False(await QueryAsync(db => db.Set<MaintenanceWorkOrder>().AnyAsync()));
    }

    [Fact]
    public async Task Generate_with_only_the_read_permission_returns_403_and_persists_nothing()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync("Overhaul", MaintenanceFrequency.Monthly, _today.AddDays(5), null, equipment.Id);

        using var response = await client.PostAsJsonAsync(
            GenerateUri,
            new GeneratePreventiveWorkRequest { Through = _today.AddDays(10) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await QueryAsync(db => db.Set<MaintenanceWorkOrder>().AnyAsync()));
    }

    [Fact]
    public async Task Replacement_returns_201_for_a_cancelled_occurrence()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");
        var scheduleId = await SeedScheduleAsync(
            "Overhaul",
            MaintenanceFrequency.Monthly,
            _today.AddDays(5),
            null,
            equipment.Id);
        var generated = Assert.Single((await GenerateAsync(client, _today.AddDays(10))).Results);
        await PostActionAsync(client, generated.WorkOrderId, "cancel");

        using var response = await client.PostAsJsonAsync(
            ReplacementUri(generated.OccurrenceId),
            new GeneratePreventiveWorkRequest(),
            CancellationToken.None);
        var replacement = await response.Content.ReadFromJsonAsync<ReplacePreventiveWorkOrderResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(replacement);
        Assert.Equal(
            $"/api/maintenance/work-orders/{replacement.WorkOrderId}",
            response.Headers.Location?.OriginalString);
        Assert.NotEqual(generated.WorkOrderId, replacement.WorkOrderId);
        Assert.Equal(generated.WorkOrderId, replacement.ReplacedWorkOrderId);
        Assert.Equal("Generated", replacement.State);

        var occurrence = await QueryAsync(db => db.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync());
        var replacementWorkOrder = await QueryAsync(db => db.Set<MaintenanceWorkOrder>()
            .AsNoTracking()
            .SingleAsync(workOrder => workOrder.Id == replacement.WorkOrderId));

        Assert.Equal(replacement.WorkOrderId, occurrence.WorkOrderId);
        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.Equal(WorkOrderStatus.Open, replacementWorkOrder.Status);
        Assert.Equal(scheduleId, replacementWorkOrder.SourceScheduleId);
        Assert.Equal(_today.AddDays(5), replacementWorkOrder.SourceOccurrenceDueDate);
    }

    [Fact]
    public async Task Replacement_returns_409_for_an_occurrence_that_is_not_cancelled()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync("Overhaul", MaintenanceFrequency.Monthly, _today.AddDays(5), null, equipment.Id);
        var generated = Assert.Single((await GenerateAsync(client, _today.AddDays(10))).Results);

        using var response = await client.PostAsJsonAsync(
            ReplacementUri(generated.OccurrenceId),
            new GeneratePreventiveWorkRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await QueryAsync(db => db.Set<MaintenanceWorkOrder>().AsNoTracking().ToListAsync()));
    }

    [Fact]
    public async Task Replacement_returns_404_for_an_unknown_occurrence()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);

        using var response = await client.PostAsJsonAsync(
            ReplacementUri(Guid.NewGuid()),
            new GeneratePreventiveWorkRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Replacement_returns_409_when_the_schedule_became_hour_based()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");
        var scheduleId = await SeedScheduleAsync(
            "Overhaul",
            MaintenanceFrequency.Monthly,
            _today.AddDays(5),
            null,
            equipment.Id);
        var generated = Assert.Single((await GenerateAsync(client, _today.AddDays(10))).Results);
        await PostActionAsync(client, generated.WorkOrderId, "cancel");
        await MakeScheduleHourBasedAsync(scheduleId);

        using var response = await client.PostAsJsonAsync(
            ReplacementUri(generated.OccurrenceId),
            new GeneratePreventiveWorkRequest(),
            CancellationToken.None);

        var occurrence = await QueryAsync(db => db.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ScheduleOccurrenceState.Cancelled, occurrence.State);
    }

    [Fact]
    public async Task A_generated_work_order_completes_through_the_existing_lifecycle_and_advances_the_schedule_once()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync("Overhaul", MaintenanceFrequency.Monthly, _today.AddDays(5), null, equipment.Id);
        var generated = Assert.Single((await GenerateAsync(client, _today.AddDays(10))).Results);

        await PostActionAsync(client, generated.WorkOrderId, "start");
        using var completion = await CompleteAsync(client, generated.WorkOrderId);
        var regenerated = await GenerateAsync(client, _today.AddDays(10));

        var occurrence = await QueryAsync(db => db.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync());
        var schedule = await QueryAsync(db => db.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync());

        Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
        Assert.Equal(ScheduleOccurrenceState.Completed, occurrence.State);
        Assert.Equal(_today.AddDays(5).AddMonths(1), schedule.NextDueDate);
        Assert.Equal(0, regenerated.CreatedCount);
        Assert.Equal("Completed", Assert.Single(regenerated.Results).State);
        Assert.Single(await QueryAsync(db => db.Set<MaintenanceWorkOrder>().AsNoTracking().ToListAsync()));
    }

    [Fact]
    public async Task Cancelling_keeps_the_schedule_due_date_until_the_replacement_is_completed()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync("Overhaul", MaintenanceFrequency.Monthly, _today.AddDays(5), null, equipment.Id);
        var generated = Assert.Single((await GenerateAsync(client, _today.AddDays(10))).Results);

        await PostActionAsync(client, generated.WorkOrderId, "cancel");
        var afterCancel = await QueryAsync(db => db.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync());

        using var replacementResponse = await client.PostAsJsonAsync(
            ReplacementUri(generated.OccurrenceId),
            new GeneratePreventiveWorkRequest(),
            CancellationToken.None);
        var replacement = (await replacementResponse.Content.ReadFromJsonAsync<ReplacePreventiveWorkOrderResponse>(
            CancellationToken.None))!;
        using var completion = await CompleteAsync(client, replacement.WorkOrderId);

        var afterCompletion = await QueryAsync(db => db.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync());
        var occurrence = await QueryAsync(db => db.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync());

        Assert.Equal(_today.AddDays(5), afterCancel.NextDueDate);
        Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
        Assert.Equal(ScheduleOccurrenceState.Completed, occurrence.State);
        Assert.Equal(replacement.WorkOrderId, occurrence.WorkOrderId);
        Assert.Equal(_today.AddDays(5).AddMonths(1), afterCompletion.NextDueDate);
    }

    private static Uri WorkOrderActionUri(Guid workOrderId, string action)
    {
        return new Uri($"/api/maintenance/work-orders/{workOrderId}/{action}", UriKind.Relative);
    }

    private static Uri ReplacementUri(Guid occurrenceId)
    {
        return new Uri($"/api/maintenance/preventive-work/occurrences/{occurrenceId}/replacement", UriKind.Relative);
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

    private static async Task PostActionAsync(HttpClient client, Guid workOrderId, string action)
    {
        using var response = await client.PostAsync(
            WorkOrderActionUri(workOrderId, action),
            content: null,
            CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid workOrderId)
    {
        return client.PostAsJsonAsync(
            WorkOrderActionUri(workOrderId, "completion"),
            new CompleteMaintenanceWorkOrderRequest
            {
                TechnicianId = Guid.NewGuid(),
                CompletedUtc = DateTime.UtcNow,
                Notes = "Preventive maintenance done.",
                Cost = 100m,
                LaborHours = 2m,
            },
            CancellationToken.None);
    }

    private async Task<T> QueryAsync<T>(Func<HarborOpsDbContext, Task<T>> query)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await query(dbContext);
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

    private async Task MakeScheduleHourBasedAsync(Guid scheduleId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var schedule = await dbContext.Set<MaintenanceSchedule>().FindAsync([scheduleId], CancellationToken.None);

        typeof(MaintenanceSchedule)
            .GetProperty(nameof(MaintenanceSchedule.Frequency))!
            .SetValue(schedule, nameof(MaintenanceFrequency.RunningHours));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
