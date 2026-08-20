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

public sealed class UpdateScheduleNextDueEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public UpdateScheduleNextDueEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_returns_200_and_advances_the_next_due_date()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var scheduleId = await SeedScheduleAsync(
            MaintenanceFrequency.Monthly,
            new DateOnly(2026, 8, 1),
            null);

        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/maintenance/schedules/{scheduleId}/next-due", UriKind.Relative),
            new { completedOn = "2026-08-18" },
            CancellationToken.None);

        var result = await response.Content
            .ReadFromJsonAsync<UpdateScheduleNextDueResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(new DateOnly(2026, 9, 18), result.NextDueDate);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var stored = await dbContext.Set<MaintenanceSchedule>().SingleAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 18), stored.NextDueDate);
    }

    [Fact]
    public async Task Post_returns_400_without_a_completion_date()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var scheduleId = await SeedScheduleAsync(
            MaintenanceFrequency.Monthly,
            new DateOnly(2026, 8, 1),
            null);

        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/maintenance/schedules/{scheduleId}/next-due", UriKind.Relative),
            new { nextDueHours = 100 },
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(UpdateScheduleNextDueValidator.CompletedOnField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_400_when_a_running_hours_schedule_has_no_hours()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var scheduleId = await SeedScheduleAsync(MaintenanceFrequency.RunningHours, null, 500);

        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/maintenance/schedules/{scheduleId}/next-due", UriKind.Relative),
            new { completedOn = "2026-08-18" },
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(UpdateScheduleNextDueValidator.NextDueHoursField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_404_for_an_unknown_schedule()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);

        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/maintenance/schedules/{Guid.NewGuid()}/next-due", UriKind.Relative),
            new { completedOn = "2026-08-18" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/maintenance/schedules/{Guid.NewGuid()}/next-due", UriKind.Relative),
            new { completedOn = "2026-08-18" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_only_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/maintenance/schedules/{Guid.NewGuid()}/next-due", UriKind.Relative),
            new { completedOn = "2026-08-18" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> SeedScheduleAsync(
        MaintenanceFrequency frequency,
        DateOnly? nextDueDate,
        int? nextDueHours)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var schedule = MaintenanceSchedule.Create("Inspection", frequency, nextDueDate, nextDueHours);

        dbContext.Set<MaintenanceSchedule>().Add(schedule);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return schedule.Id;
    }
}
