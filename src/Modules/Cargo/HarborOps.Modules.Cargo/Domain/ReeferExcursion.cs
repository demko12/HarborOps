namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferExcursion
{
    internal const int DirectionMaxLength = 10;
    internal const int DispositionMaxLength = 50;
    internal const int ExplanationMaxLength = 1000;
    internal const int StatusMaxLength = 50;

    private ReeferExcursion()
    {
    }

    private ReeferExcursion(
        Guid id,
        Guid containerId,
        Guid readingId,
        ReeferExcursionDirection direction,
        decimal temperatureCelsius,
        decimal thresholdMinimumCelsius,
        decimal thresholdMaximumCelsius,
        DateTime measuredAtUtc,
        DateTime detectedAtUtc)
    {
        Id = id;
        ContainerId = containerId;
        ReadingId = readingId;
        Direction = direction;
        TemperatureCelsius = temperatureCelsius;
        ThresholdMinimumCelsius = thresholdMinimumCelsius;
        ThresholdMaximumCelsius = thresholdMaximumCelsius;
        MeasuredAtUtc = measuredAtUtc;
        DetectedAtUtc = detectedAtUtc;
        Status = ReeferExcursionStatus.Open;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid ContainerId { get; private set; }

    public Guid ReadingId { get; private set; }

    public ReeferExcursionDirection Direction { get; private set; }

    public decimal TemperatureCelsius { get; private set; }

    public decimal ThresholdMinimumCelsius { get; private set; }

    public decimal ThresholdMaximumCelsius { get; private set; }

    public DateTime MeasuredAtUtc { get; private set; }

    public DateTime DetectedAtUtc { get; private set; }

    public ReeferExcursionStatus Status { get; private set; }

    public ReeferExcursionDisposition? Disposition { get; private set; }

    public string? Explanation { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    public Guid? SupersededByExcursionId { get; private set; }

    public DateTime? SupersededAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    internal bool IsOpen => Status is ReeferExcursionStatus.Open;

    internal static bool BlocksRelease(ReeferExcursionStatus status)
    {
        return status is ReeferExcursionStatus.Open;
    }

    internal static ReeferExcursion Open(ReeferTemperatureReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (reading.BreachDirection is not { } direction)
        {
            throw new ReeferExcursionNotBreachedException(reading.Id);
        }

        return new ReeferExcursion(
            Guid.NewGuid(),
            reading.ContainerId,
            reading.Id,
            direction,
            reading.TemperatureCelsius,
            reading.ThresholdMinimumCelsius,
            reading.ThresholdMaximumCelsius,
            reading.MeasuredAtUtc,
            reading.RecordedAtUtc);
    }

    internal void Resolve(
        ReeferExcursionDisposition disposition,
        string explanation,
        Guid actorUserId,
        DateTime utcNow)
    {
        EnsureIsOpen(ReeferExcursionNotOpenException.ResolveAction);

        var normalizedExplanation = NormalizeOptional(explanation)
            ?? throw new ReeferExcursionExplanationRequiredException(Id);

        Status = ReeferExcursionStatus.Resolved;
        Disposition = disposition;
        Explanation = normalizedExplanation;
        ResolvedByUserId = actorUserId;
        ResolvedAtUtc = utcNow;
        Touch();
    }

    internal void Supersede(Guid? replacementExcursionId, DateTime utcNow)
    {
        EnsureIsOpen(ReeferExcursionNotOpenException.SupersedeAction);

        Status = ReeferExcursionStatus.Superseded;
        SupersededByExcursionId = replacementExcursionId;
        SupersededAtUtc = utcNow;
        Touch();
    }

    private void EnsureIsOpen(string requestedAction)
    {
        if (!IsOpen)
        {
            throw new ReeferExcursionNotOpenException(Id, Status, requestedAction);
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
