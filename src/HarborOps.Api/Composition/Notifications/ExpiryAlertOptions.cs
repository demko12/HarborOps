namespace HarborOps.Api.Composition.Notifications;

public sealed class ExpiryAlertOptions
{
    public const string SectionName = "ExpiryAlerts";
    public int IntervalMinutes { get; init; } = 60;
    public int[] ThresholdDays { get; init; } = [90, 30, 7];
}
