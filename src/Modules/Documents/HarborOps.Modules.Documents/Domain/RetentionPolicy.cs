namespace HarborOps.Modules.Documents.Domain;

public sealed class RetentionPolicy
{
    public int DocumentTypeId { get; private set; }

    public int RetentionPeriodDays { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    private RetentionPolicy()
    {
    }

    public RetentionPolicy(int documentTypeId, int retentionPeriodDays)
    {
        if (retentionPeriodDays <= 0)
        {
            throw new ArgumentException("Retention period must be greater than zero days.", nameof(retentionPeriodDays));
        }

        DocumentTypeId = documentTypeId;
        RetentionPeriodDays = retentionPeriodDays;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void UpdateRetentionPeriod(int retentionPeriodDays)
    {
        if (retentionPeriodDays <= 0)
        {
            throw new ArgumentException("Retention period must be greater than zero days.", nameof(retentionPeriodDays));
        }

        RetentionPeriodDays = retentionPeriodDays;
        ConcurrencyToken = Guid.NewGuid();
    }
}
