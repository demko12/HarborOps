namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferTemperatureReading
{
    internal const int NotesMaxLength = 1000;
    internal const int ReasonMaxLength = 500;
    internal const int StatusMaxLength = 50;

    private ReeferTemperatureReading()
    {
    }

    private ReeferTemperatureReading(
        Guid id,
        Guid containerId,
        decimal temperatureCelsius,
        DateTime measuredAtUtc,
        DateTime recordedAtUtc,
        string? notes,
        Guid recordedByUserId,
        decimal thresholdMinimumCelsius,
        decimal thresholdMaximumCelsius,
        decimal thresholdTargetCelsius,
        int thresholdProfileVersion,
        Guid? correctsReadingId)
    {
        Id = id;
        ContainerId = containerId;
        TemperatureCelsius = temperatureCelsius;
        MeasuredAtUtc = measuredAtUtc;
        RecordedAtUtc = recordedAtUtc;
        Notes = notes;
        RecordedByUserId = recordedByUserId;
        ThresholdMinimumCelsius = thresholdMinimumCelsius;
        ThresholdMaximumCelsius = thresholdMaximumCelsius;
        ThresholdTargetCelsius = thresholdTargetCelsius;
        ThresholdProfileVersion = thresholdProfileVersion;
        CorrectsReadingId = correctsReadingId;
        Status = ReeferReadingStatus.Effective;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid ContainerId { get; private set; }

    public decimal TemperatureCelsius { get; private set; }

    public DateTime MeasuredAtUtc { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    public string? Notes { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public decimal ThresholdMinimumCelsius { get; private set; }

    public decimal ThresholdMaximumCelsius { get; private set; }

    public decimal ThresholdTargetCelsius { get; private set; }

    public int ThresholdProfileVersion { get; private set; }

    public ReeferReadingStatus Status { get; private set; }

    public Guid? CorrectsReadingId { get; private set; }

    public Guid? SupersededByReadingId { get; private set; }

    public string? CorrectionReason { get; private set; }

    public Guid? CorrectedByUserId { get; private set; }

    public DateTime? CorrectedAtUtc { get; private set; }

    public string? VoidReason { get; private set; }

    public Guid? VoidedByUserId { get; private set; }

    public DateTime? VoidedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    internal bool IsEffective => Status is ReeferReadingStatus.Effective;

    internal bool IsWithinThresholds =>
        TemperatureCelsius >= ThresholdMinimumCelsius
        && TemperatureCelsius <= ThresholdMaximumCelsius;

    internal ReeferExcursionDirection? BreachDirection => IsWithinThresholds
        ? null
        : TemperatureCelsius < ThresholdMinimumCelsius
            ? ReeferExcursionDirection.Below
            : ReeferExcursionDirection.Above;

    internal bool OpensExcursion => IsEffective && !IsWithinThresholds;

    internal static ReeferTemperatureReading Record(
        ReeferMonitoringProfile profile,
        decimal temperatureCelsius,
        DateTime measuredAtUtc,
        string? notes,
        Guid actorUserId,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(profile);

        EnsureTemperatureIsValid(profile.ContainerId, temperatureCelsius);
        EnsureMeasuredAtIsNotInFuture(profile.ContainerId, measuredAtUtc, utcNow);

        return new ReeferTemperatureReading(
            Guid.NewGuid(),
            profile.ContainerId,
            temperatureCelsius,
            measuredAtUtc,
            utcNow,
            NormalizeOptional(notes),
            actorUserId,
            profile.MinimumCelsius,
            profile.MaximumCelsius,
            profile.TargetCelsius,
            profile.Version,
            null);
    }

    internal ReeferTemperatureReading Correct(
        decimal temperatureCelsius,
        DateTime measuredAtUtc,
        string? notes,
        string reason,
        Guid actorUserId,
        DateTime utcNow)
    {
        EnsureIsEffective(ReeferReadingNotEffectiveException.CorrectAction);
        EnsureTemperatureIsValid(ContainerId, temperatureCelsius);
        EnsureMeasuredAtIsNotInFuture(ContainerId, measuredAtUtc, utcNow);

        var normalizedReason = NormalizeOptional(reason)
            ?? throw new ReeferReadingReasonRequiredException(
                Id,
                ReeferReadingNotEffectiveException.CorrectAction);

        var replacement = new ReeferTemperatureReading(
            Guid.NewGuid(),
            ContainerId,
            temperatureCelsius,
            measuredAtUtc,
            utcNow,
            NormalizeOptional(notes),
            actorUserId,
            ThresholdMinimumCelsius,
            ThresholdMaximumCelsius,
            ThresholdTargetCelsius,
            ThresholdProfileVersion,
            Id);

        Status = ReeferReadingStatus.Superseded;
        SupersededByReadingId = replacement.Id;
        CorrectionReason = normalizedReason;
        CorrectedByUserId = actorUserId;
        CorrectedAtUtc = utcNow;
        Touch();

        return replacement;
    }

    internal void Void(string reason, Guid actorUserId, DateTime utcNow)
    {
        EnsureIsEffective(ReeferReadingNotEffectiveException.VoidAction);

        var normalizedReason = NormalizeOptional(reason)
            ?? throw new ReeferReadingReasonRequiredException(
                Id,
                ReeferReadingNotEffectiveException.VoidAction);

        Status = ReeferReadingStatus.Voided;
        VoidReason = normalizedReason;
        VoidedByUserId = actorUserId;
        VoidedAtUtc = utcNow;
        Touch();
    }

    private void EnsureIsEffective(string requestedAction)
    {
        if (!IsEffective)
        {
            throw new ReeferReadingNotEffectiveException(Id, Status, requestedAction);
        }
    }

    private static void EnsureTemperatureIsValid(Guid containerId, decimal temperatureCelsius)
    {
        if (!ReeferTemperature.IsWithinSupportedRange(temperatureCelsius))
        {
            throw new ReeferReadingNotValidException(
                containerId,
                ReeferReadingNotValidException.OutOfSupportedRangeReason);
        }

        if (!ReeferTemperature.HasSupportedPrecision(temperatureCelsius))
        {
            throw new ReeferReadingNotValidException(
                containerId,
                ReeferReadingNotValidException.PrecisionNotSupportedReason);
        }
    }

    private static void EnsureMeasuredAtIsNotInFuture(
        Guid containerId,
        DateTime measuredAtUtc,
        DateTime utcNow)
    {
        if (measuredAtUtc > utcNow)
        {
            throw new ReeferReadingNotValidException(
                containerId,
                ReeferReadingNotValidException.MeasuredInFutureReason);
        }
    }

    private void Touch()
    {
        ConcurrencyToken = Guid.NewGuid();
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();

        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
