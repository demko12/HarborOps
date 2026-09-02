using System.Net.Http.Json;

namespace HarborOps.Web.Api;

internal sealed class NotificationsApiClient(HttpClient httpClient)
{
    internal async Task<NotificationsPage?> GetAsync(string status, int pageNumber, int pageSize, CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<NotificationsPage>($"api/notifications?status={Uri.EscapeDataString(status)}&pageNumber={pageNumber}&pageSize={pageSize}", cancellationToken);

    internal async Task AcknowledgeAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync($"api/notifications/{notificationId}/acknowledge", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    internal async Task AcknowledgeAllAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync("api/notifications/acknowledge-all", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

internal sealed record NotificationsPage(IReadOnlyList<NotificationItem> Items, int PageNumber, int PageSize, int TotalCount, int UnreadCount);
internal sealed record NotificationItem(Guid NotificationId, string Capability, string SourceType, Guid SourceId, Guid? VesselId, Guid? CrewMemberId, DateOnly ExpiryDate, int ThresholdDays, string Severity, DateTime GeneratedAtUtc, bool IsAcknowledged);
