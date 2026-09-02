namespace HarborOps.Modules.Notifications.Domain;

public sealed class NotificationAcknowledgement
{
    public Guid AlertId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime AcknowledgedAtUtc { get; private set; }
    private NotificationAcknowledgement() { }
    public NotificationAcknowledgement(Guid alertId, Guid userId, DateTime acknowledgedAtUtc) { AlertId = alertId; UserId = userId; AcknowledgedAtUtc = acknowledgedAtUtc; }
}
