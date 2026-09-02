using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Notifications.Contracts;
using HarborOps.Modules.Notifications.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Notifications;

public sealed class NotificationsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory application;

    public NotificationsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        this.application = application;
    }

    [Fact]
    public async Task Get_returns_only_alerts_for_capabilities_the_user_can_read()
    {
        var documentAlert = await SeedAlertAsync("documents");
        await SeedAlertAsync("crew");
        using var client = await application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var response = await client.GetAsync("/api/notifications?status=unread&pageNumber=1&pageSize=25", CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<NotificationsPage>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.UnreadCount);
        Assert.Equal(documentAlert.Id, Assert.Single(page.Items).NotificationId);
    }

    [Fact]
    public async Task Get_returns_bad_request_for_an_invalid_status_and_an_empty_page_without_source_permissions()
    {
        using var permittedClient = await application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);
        using var invalidResponse = await permittedClient.GetAsync("/api/notifications?status=bad", CancellationToken.None);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        using var noSourcePermissionClient = await application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Voyages.Read);
        using var emptyResponse = await noSourcePermissionClient.GetAsync("/api/notifications", CancellationToken.None);
        var page = await emptyResponse.Content.ReadFromJsonAsync<NotificationsPage>(CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.NotNull(page);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Acknowledge_returns_no_content_for_a_visible_alert_and_not_found_for_an_unreadable_one()
    {
        var documentAlert = await SeedAlertAsync("documents");
        var crewAlert = await SeedAlertAsync("crew");
        using var client = await application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var accepted = await client.PostAsync($"/api/notifications/{documentAlert.Id}/acknowledge", null, CancellationToken.None);
        using var hidden = await client.PostAsync($"/api/notifications/{crewAlert.Id}/acknowledge", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    private async Task<OperationAlert> SeedAlertAsync(string capability)
    {
        var alert = new OperationAlert(capability, "certificate", Guid.NewGuid(), Guid.NewGuid(), null, new(2026, 9, 8), 7, "critical", HarborOpsApiFactory.FixedUtcNow);
        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        await dbContext.AddAsync(alert);
        await dbContext.SaveChangesAsync();
        return alert;
    }
}
