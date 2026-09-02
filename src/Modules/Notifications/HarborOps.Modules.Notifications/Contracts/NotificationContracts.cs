namespace HarborOps.Modules.Notifications.Contracts;

public sealed record GetNotificationsRequest(string? Status, int? PageNumber, int? PageSize);
public sealed record OperationAlertItem(Guid NotificationId, string Capability, string SourceType, Guid SourceId, Guid? VesselId, Guid? CrewMemberId, DateOnly ExpiryDate, int ThresholdDays, string Severity, DateTime GeneratedAtUtc, bool IsAcknowledged);
public sealed record NotificationsPage(IReadOnlyList<OperationAlertItem> Items, int PageNumber, int PageSize, int TotalCount, int UnreadCount);
