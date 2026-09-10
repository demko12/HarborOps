using HarborOps.Modules.Documents.Domain;

namespace HarborOps.Modules.Documents.Application;

internal static class RetentionLifecycleCalculator
{
    internal static DateTime? ComputeTriggerAtUtc(
        string status,
        DateTime? supersededAtUtc,
        DateTime? revokedAtUtc,
        DateOnly expiryDate,
        DateTime nowUtc)
    {
        if (string.Equals(status, Document.SupersededStatus, StringComparison.Ordinal))
        {
            return supersededAtUtc;
        }

        if (string.Equals(status, Document.RevokedStatus, StringComparison.Ordinal))
        {
            return revokedAtUtc;
        }

        var expiryAtUtc = expiryDate.ToDateTime(TimeOnly.MinValue);

        return expiryAtUtc < nowUtc ? expiryAtUtc : null;
    }

    internal static DateTime ComputeDueAtUtc(DateTime triggerAtUtc, int retentionPeriodDays) =>
        triggerAtUtc.AddDays(retentionPeriodDays);
}
