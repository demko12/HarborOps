namespace HarborOps.Modules.Documents.Domain;

public sealed class Notification
{
    internal const int ChannelMaxLength = 50;

    public int Id { get; private set; }

    public int DocumentId { get; private set; }

    public Document Document { get; private set; } = null!;

    public int UserId { get; private set; }

    public int DaysBeforeExpiry { get; private set; }

    public string Channel { get; private set; } = string.Empty;

    public DateTime SentAt { get; private set; }

    private Notification()
    {
    }

    public Notification(int documentId, int userId, int daysBeforeExpiry, string channel, DateTime sentAt)
    {
        DocumentId = documentId;
        UserId = userId;
        DaysBeforeExpiry = daysBeforeExpiry;
        Channel = channel;
        SentAt = sentAt;
    }
}
