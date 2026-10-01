namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoDiscrepancy
{
    internal const int TypeMaxLength = 50;
    internal const int SeverityMaxLength = 50;
    internal const int StatusMaxLength = 50;
    internal const int DispositionMaxLength = 50;
    internal const int DescriptionMaxLength = 1000;
    internal const int ResolutionSummaryMinLength = 20;
    internal const int ActorNameMaxLength = 256;

    private CargoDiscrepancy()
    {
    }

    private CargoDiscrepancy(
        Guid id,
        Guid cargoBookingId,
        Guid? containerId,
        CargoDiscrepancyType type,
        CargoDiscrepancySeverity severity,
        string description,
        DateTime observedAtUtc,
        Guid reportedByUserId,
        string? reportedByName,
        DateTime reportedAtUtc)
    {
        Id = id;
        CargoBookingId = cargoBookingId;
        ContainerId = containerId;
        Type = type;
        Severity = severity;
        Description = description;
        ObservedAtUtc = observedAtUtc;
        ReportedByUserId = reportedByUserId;
        ReportedByName = reportedByName;
        ReportedAtUtc = reportedAtUtc;
        Status = CargoDiscrepancyStatus.Reported;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid CargoBookingId { get; private set; }

    public Guid? ContainerId { get; private set; }

    public CargoDiscrepancyType Type { get; private set; }

    public CargoDiscrepancySeverity Severity { get; private set; }

    public string Description { get; private set; } = null!;

    public DateTime ObservedAtUtc { get; private set; }

    public Guid ReportedByUserId { get; private set; }

    public string? ReportedByName { get; private set; }

    public DateTime ReportedAtUtc { get; private set; }

    public CargoDiscrepancyStatus Status { get; private set; }

    public CargoDiscrepancyDisposition? Disposition { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    public string? ResolvedByName { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    public Guid? VoidedByUserId { get; private set; }

    public string? VoidedByName { get; private set; }

    public DateTime? VoidedAtUtc { get; private set; }

    public Guid? SourceExcursionId { get; private set; }

    public ReeferExcursionDirection? SourceDirection { get; private set; }

    public decimal? SourceTemperatureCelsius { get; private set; }

    public decimal? SourceThresholdMinimumCelsius { get; private set; }

    public decimal? SourceThresholdMaximumCelsius { get; private set; }

    public DateTime? SourceMeasuredAtUtc { get; private set; }

    public DateTime? SourceDetectedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    internal bool IsOpen => BlocksCompletion(Status);

    internal static bool BlocksCompletion(CargoDiscrepancyStatus status)
    {
        return status is CargoDiscrepancyStatus.Reported or CargoDiscrepancyStatus.Investigating;
    }

    internal static bool RequiresContainer(CargoDiscrepancyType type)
    {
        return type is CargoDiscrepancyType.SealMismatch or CargoDiscrepancyType.TemperatureRelated;
    }

    internal static (CargoDiscrepancy Discrepancy, CargoDiscrepancyEvent Reported) Register(
        CargoBooking booking,
        Container? container,
        ReeferExcursion? sourceExcursion,
        CargoDiscrepancyType type,
        CargoDiscrepancySeverity severity,
        string description,
        DateTime observedAtUtc,
        Guid reportedByUserId,
        string? reportedByName,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(description);

        if (!booking.AcceptsDiscrepancies)
        {
            throw new CargoDiscrepancyRegistrationNotAllowedException(booking.Id, booking.Status);
        }

        if (container is null && RequiresContainer(type))
        {
            throw new CargoDiscrepancyContainerRequiredException(type);
        }

        if (container is not null && container.CargoBookingId != booking.Id)
        {
            throw new CargoDiscrepancyContainerMismatchException(container.Id, booking.Id);
        }

        if (sourceExcursion is { Status: ReeferExcursionStatus.Superseded })
        {
            throw new CargoDiscrepancySourceSupersededException(
                sourceExcursion.Id,
                sourceExcursion.SupersededByExcursionId);
        }

        if (sourceExcursion is not null
            && (type is not CargoDiscrepancyType.TemperatureRelated
                || container is null
                || sourceExcursion.ContainerId != container.Id))
        {
            throw new CargoDiscrepancySourceMismatchException(sourceExcursion.Id, container?.Id, type);
        }

        var normalizedReporterName = NormalizeActorName(reportedByName);

        var discrepancy = new CargoDiscrepancy(
            Guid.NewGuid(),
            booking.Id,
            container?.Id,
            type,
            severity,
            description.Trim(),
            observedAtUtc,
            reportedByUserId,
            normalizedReporterName,
            utcNow);

        if (sourceExcursion is not null)
        {
            discrepancy.SourceExcursionId = sourceExcursion.Id;
            discrepancy.SourceDirection = sourceExcursion.Direction;
            discrepancy.SourceTemperatureCelsius = sourceExcursion.TemperatureCelsius;
            discrepancy.SourceThresholdMinimumCelsius = sourceExcursion.ThresholdMinimumCelsius;
            discrepancy.SourceThresholdMaximumCelsius = sourceExcursion.ThresholdMaximumCelsius;
            discrepancy.SourceMeasuredAtUtc = sourceExcursion.MeasuredAtUtc;
            discrepancy.SourceDetectedAtUtc = sourceExcursion.DetectedAtUtc;
        }

        var reported = CargoDiscrepancyEvent.Record(
            discrepancy.Id,
            CargoDiscrepancyEventType.Reported,
            null,
            CargoDiscrepancyStatus.Reported,
            reportedByUserId,
            normalizedReporterName,
            utcNow);

        return (discrepancy, reported);
    }

    internal CargoDiscrepancyEvent StartInvestigation(Guid actorUserId, string? actorName, DateTime utcNow)
    {
        if (Status is not CargoDiscrepancyStatus.Reported)
        {
            throw NotAllowed(
                CargoDiscrepancyTransitionNotAllowedException.StartInvestigationAction,
                Status is CargoDiscrepancyStatus.Investigating
                    ? CargoDiscrepancyTransitionNotAllowedException.AlreadyInvestigatingReason
                    : ClosedReason());
        }

        return MoveTo(
            CargoDiscrepancyStatus.Investigating,
            CargoDiscrepancyEventType.InvestigationStarted,
            actorUserId,
            actorName,
            utcNow,
            text: null);
    }

    internal CargoDiscrepancyEvent AddNote(string note, Guid actorUserId, string? actorName, DateTime utcNow)
    {
        if (!IsOpen)
        {
            throw NotAllowed(
                CargoDiscrepancyTransitionNotAllowedException.AddNoteAction,
                ClosedReason());
        }

        var normalizedNote = NormalizeOptional(note)
            ?? throw new CargoDiscrepancyTextRequiredException(
                Id,
                CargoDiscrepancyTransitionNotAllowedException.AddNoteAction,
                CargoDiscrepancyTextRequiredException.NoteRequirement);

        Touch();

        return CargoDiscrepancyEvent.Record(
            Id,
            CargoDiscrepancyEventType.NoteAdded,
            Status,
            Status,
            actorUserId,
            NormalizeActorName(actorName),
            utcNow,
            normalizedNote);
    }

    internal CargoDiscrepancyEvent Resolve(
        CargoDiscrepancyDisposition disposition,
        string summary,
        int investigationNoteCount,
        ReeferExcursionStatus? sourceExcursionStatus,
        Guid actorUserId,
        string? actorName,
        DateTime utcNow)
    {
        if (Status is not CargoDiscrepancyStatus.Investigating)
        {
            throw NotAllowed(
                CargoDiscrepancyTransitionNotAllowedException.ResolveAction,
                Status is CargoDiscrepancyStatus.Reported
                    ? CargoDiscrepancyTransitionNotAllowedException.NotInvestigatingReason
                    : ClosedReason());
        }

        if (investigationNoteCount <= 0)
        {
            throw NotAllowed(
                CargoDiscrepancyTransitionNotAllowedException.ResolveAction,
                CargoDiscrepancyTransitionNotAllowedException.NoInvestigationNoteReason);
        }

        if (SourceExcursionId is { } excursionId
            && sourceExcursionStatus is ReeferExcursionStatus.Open)
        {
            throw new CargoDiscrepancySourceExcursionOpenException(Id, excursionId);
        }

        var normalizedSummary = NormalizeOptional(summary);

        if (normalizedSummary is null || normalizedSummary.Length < ResolutionSummaryMinLength)
        {
            throw new CargoDiscrepancyTextRequiredException(
                Id,
                CargoDiscrepancyTransitionNotAllowedException.ResolveAction,
                CargoDiscrepancyTextRequiredException.ResolutionSummaryRequirement);
        }

        Disposition = disposition;
        ResolvedByUserId = actorUserId;
        ResolvedByName = NormalizeActorName(actorName);
        ResolvedAtUtc = utcNow;

        return MoveTo(
            CargoDiscrepancyStatus.Resolved,
            CargoDiscrepancyEventType.Resolved,
            actorUserId,
            actorName,
            utcNow,
            normalizedSummary);
    }

    internal CargoDiscrepancyEvent Void(string reason, Guid actorUserId, string? actorName, DateTime utcNow)
    {
        if (!IsOpen)
        {
            throw NotAllowed(
                CargoDiscrepancyTransitionNotAllowedException.VoidAction,
                ClosedReason());
        }

        var normalizedReason = NormalizeOptional(reason)
            ?? throw new CargoDiscrepancyTextRequiredException(
                Id,
                CargoDiscrepancyTransitionNotAllowedException.VoidAction,
                CargoDiscrepancyTextRequiredException.VoidReasonRequirement);

        VoidedByUserId = actorUserId;
        VoidedByName = NormalizeActorName(actorName);
        VoidedAtUtc = utcNow;

        return MoveTo(
            CargoDiscrepancyStatus.Voided,
            CargoDiscrepancyEventType.Voided,
            actorUserId,
            actorName,
            utcNow,
            normalizedReason);
    }

    private CargoDiscrepancyEvent MoveTo(
        CargoDiscrepancyStatus toStatus,
        CargoDiscrepancyEventType eventType,
        Guid actorUserId,
        string? actorName,
        DateTime utcNow,
        string? text)
    {
        var fromStatus = Status;

        Status = toStatus;
        Touch();

        return CargoDiscrepancyEvent.Record(
            Id,
            eventType,
            fromStatus,
            toStatus,
            actorUserId,
            NormalizeActorName(actorName),
            utcNow,
            text);
    }

    private CargoDiscrepancyTransitionNotAllowedException NotAllowed(string action, string reason)
    {
        return new CargoDiscrepancyTransitionNotAllowedException(Id, Status, action, reason);
    }

    private string ClosedReason()
    {
        return Status is CargoDiscrepancyStatus.Voided
            ? CargoDiscrepancyTransitionNotAllowedException.AlreadyVoidedReason
            : CargoDiscrepancyTransitionNotAllowedException.AlreadyResolvedReason;
    }

    private void Touch()
    {
        ConcurrencyToken = Guid.NewGuid();
    }

    private static string? NormalizeActorName(string? actorName)
    {
        var normalized = NormalizeOptional(actorName);

        return normalized is { Length: > ActorNameMaxLength } ? normalized[..ActorNameMaxLength] : normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();

        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
