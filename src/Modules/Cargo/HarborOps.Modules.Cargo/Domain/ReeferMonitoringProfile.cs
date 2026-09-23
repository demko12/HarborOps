namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferMonitoringProfile
{
    internal const int InitialVersion = 1;
    internal const int ReasonMaxLength = 500;

    private ReeferMonitoringProfile()
    {
    }

    private ReeferMonitoringProfile(
        Guid id,
        Guid containerId,
        decimal minimumCelsius,
        decimal maximumCelsius,
        decimal targetCelsius,
        Guid actorUserId,
        DateTime utcNow)
    {
        Id = id;
        ContainerId = containerId;
        MinimumCelsius = minimumCelsius;
        MaximumCelsius = maximumCelsius;
        TargetCelsius = targetCelsius;
        Version = InitialVersion;
        CreatedByUserId = actorUserId;
        CreatedAtUtc = utcNow;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = utcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid ContainerId { get; private set; }

    public decimal MinimumCelsius { get; private set; }

    public decimal MaximumCelsius { get; private set; }

    public decimal TargetCelsius { get; private set; }

    public int Version { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Guid UpdatedByUserId { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    internal static ReeferMonitoringProfile Configure(
        Guid containerId,
        bool isReefer,
        decimal minimumCelsius,
        decimal maximumCelsius,
        decimal targetCelsius,
        Guid actorUserId,
        DateTime utcNow)
    {
        if (!isReefer)
        {
            throw new ReeferMonitoringNotAllowedException(containerId);
        }

        EnsureThresholdsAreValid(containerId, minimumCelsius, maximumCelsius, targetCelsius);

        return new ReeferMonitoringProfile(
            Guid.NewGuid(),
            containerId,
            minimumCelsius,
            maximumCelsius,
            targetCelsius,
            actorUserId,
            utcNow);
    }

    internal ReeferProfileChange RecordConfiguration(string? reason)
    {
        return ReeferProfileChange.Record(
            Id,
            Version,
            null,
            null,
            null,
            MinimumCelsius,
            MaximumCelsius,
            TargetCelsius,
            NormalizeOptional(reason),
            CreatedByUserId,
            CreatedAtUtc);
    }

    internal ReeferProfileChange Revise(
        decimal minimumCelsius,
        decimal maximumCelsius,
        decimal targetCelsius,
        string reason,
        Guid actorUserId,
        DateTime utcNow)
    {
        EnsureThresholdsAreValid(ContainerId, minimumCelsius, maximumCelsius, targetCelsius);

        var normalizedReason = NormalizeOptional(reason)
            ?? throw new ReeferProfileChangeReasonRequiredException(Id);

        var fromMinimumCelsius = MinimumCelsius;
        var fromMaximumCelsius = MaximumCelsius;
        var fromTargetCelsius = TargetCelsius;

        MinimumCelsius = minimumCelsius;
        MaximumCelsius = maximumCelsius;
        TargetCelsius = targetCelsius;
        Version++;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = utcNow;
        ConcurrencyToken = Guid.NewGuid();

        return ReeferProfileChange.Record(
            Id,
            Version,
            fromMinimumCelsius,
            fromMaximumCelsius,
            fromTargetCelsius,
            MinimumCelsius,
            MaximumCelsius,
            TargetCelsius,
            normalizedReason,
            actorUserId,
            utcNow);
    }

    private static void EnsureThresholdsAreValid(
        Guid containerId,
        decimal minimumCelsius,
        decimal maximumCelsius,
        decimal targetCelsius)
    {
        if (!ReeferTemperature.IsWithinSupportedRange(minimumCelsius)
            || !ReeferTemperature.IsWithinSupportedRange(maximumCelsius)
            || !ReeferTemperature.IsWithinSupportedRange(targetCelsius))
        {
            throw new ReeferThresholdsNotValidException(
                containerId,
                ReeferThresholdsNotValidException.OutOfSupportedRangeReason);
        }

        if (!ReeferTemperature.HasSupportedPrecision(minimumCelsius)
            || !ReeferTemperature.HasSupportedPrecision(maximumCelsius)
            || !ReeferTemperature.HasSupportedPrecision(targetCelsius))
        {
            throw new ReeferThresholdsNotValidException(
                containerId,
                ReeferThresholdsNotValidException.PrecisionNotSupportedReason);
        }

        if (minimumCelsius > maximumCelsius)
        {
            throw new ReeferThresholdsNotValidException(
                containerId,
                ReeferThresholdsNotValidException.MinimumAboveMaximumReason);
        }

        if (targetCelsius < minimumCelsius || targetCelsius > maximumCelsius)
        {
            throw new ReeferThresholdsNotValidException(
                containerId,
                ReeferThresholdsNotValidException.TargetOutOfRangeReason);
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();

        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
