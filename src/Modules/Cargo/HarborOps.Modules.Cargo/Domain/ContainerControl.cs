namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ContainerControl
{
    internal const int InspectionNotesMaxLength = 1000;
    internal const int InspectionResultMaxLength = 50;
    internal const int ReasonMaxLength = 500;
    internal const int SealNumberMaxLength = 30;
    internal const int SealStatusMaxLength = 50;
    internal const int StateMaxLength = 50;

    internal const decimal MinimumTemperatureCelsius = -60m;
    internal const decimal MaximumTemperatureCelsius = 60m;
    internal const int TemperaturePrecision = 5;
    internal const int TemperatureScale = 2;

    private ContainerControl()
    {
    }

    private ContainerControl(Guid id, Guid containerId)
    {
        Id = id;
        ContainerId = containerId;
        SealStatus = ContainerSealStatus.None;
        State = ContainerControlState.Pending;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid ContainerId { get; private set; }

    public string? SealNumber { get; private set; }

    public ContainerSealStatus SealStatus { get; private set; }

    public ContainerControlState State { get; private set; }

    public ContainerInspectionResult? LatestInspectionResult { get; private set; }

    public string? LatestInspectionNotes { get; private set; }

    public decimal? LatestInspectionTemperatureCelsius { get; private set; }

    public DateTime? LatestInspectedAtUtc { get; private set; }

    public Guid? LatestInspectedByUserId { get; private set; }

    public string? StateReason { get; private set; }

    public Guid? StateChangedByUserId { get; private set; }

    public DateTime? StateChangedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    internal bool HasIntactSeal => SealStatus is ContainerSealStatus.Intact;

    internal bool HasPassedInspection =>
        LatestInspectionResult is ContainerInspectionResult.Passed;

    internal string? ReleaseBlocker => State switch
    {
        ContainerControlState.Held =>
            ContainerControlTransitionNotAllowedException.HeldReason,
        ContainerControlState.Released =>
            ContainerControlTransitionNotAllowedException.AlreadyReleasedReason,
        _ when !HasIntactSeal =>
            ContainerControlTransitionNotAllowedException.SealNotIntactReason,
        _ when LatestInspectionResult is null =>
            ContainerControlTransitionNotAllowedException.InspectionMissingReason,
        _ when !HasPassedInspection =>
            ContainerControlTransitionNotAllowedException.InspectionNotPassedReason,
        _ => null,
    };

    internal bool CanRelease => ReleaseBlocker is null;

    internal static bool AllowsLoading(ContainerControlState? state)
    {
        return state is ContainerControlState.Released;
    }

    internal static ContainerControl Start(Guid containerId)
    {
        return new ContainerControl(Guid.NewGuid(), containerId);
    }

    internal ContainerControlEvent AssignSeal(
        string sealNumber,
        Guid actorUserId,
        DateTime utcNow)
    {
        var normalizedSealNumber = Container.NormalizeSealNumber(sealNumber);

        if (HasIntactSeal
            && string.Equals(SealNumber, normalizedSealNumber, StringComparison.Ordinal))
        {
            throw new ContainerControlTransitionNotAllowedException(
                Id,
                State,
                ContainerControlTransitionNotAllowedException.AssignSealAction,
                ContainerControlTransitionNotAllowedException.SealAlreadyAssignedReason);
        }

        var fromState = State;

        SealNumber = normalizedSealNumber;
        SealStatus = ContainerSealStatus.Intact;
        RevokeRelease(actorUserId, utcNow);
        Touch();

        return ContainerControlEvent.Record(
            Id,
            ContainerControlAction.SealAssigned,
            fromState,
            State,
            actorUserId,
            utcNow,
            sealNumber: normalizedSealNumber);
    }

    internal ContainerControlEvent BreakSeal(Guid actorUserId, DateTime utcNow, string? notes)
    {
        if (!HasIntactSeal)
        {
            throw new ContainerControlTransitionNotAllowedException(
                Id,
                State,
                ContainerControlTransitionNotAllowedException.BreakSealAction,
                SealStatus is ContainerSealStatus.Broken
                    ? ContainerControlTransitionNotAllowedException.SealAlreadyBrokenReason
                    : ContainerControlTransitionNotAllowedException.SealNotIntactReason);
        }

        var fromState = State;

        SealStatus = ContainerSealStatus.Broken;
        RevokeRelease(actorUserId, utcNow);
        Touch();

        return ContainerControlEvent.Record(
            Id,
            ContainerControlAction.SealBroken,
            fromState,
            State,
            actorUserId,
            utcNow,
            sealNumber: SealNumber,
            notes: NormalizeOptional(notes));
    }

    internal IReadOnlyList<ContainerControlEvent> RecordInspection(
        ContainerInspectionResult result,
        string? conditionNotes,
        decimal? temperatureCelsius,
        bool isReefer,
        Guid actorUserId,
        DateTime utcNow)
    {
        if (temperatureCelsius is not null && !isReefer)
        {
            throw new ContainerTemperatureNotAllowedException(Id);
        }

        var fromState = State;
        var normalizedNotes = NormalizeOptional(conditionNotes);

        LatestInspectionResult = result;
        LatestInspectionNotes = normalizedNotes;
        LatestInspectionTemperatureCelsius = temperatureCelsius;
        LatestInspectedAtUtc = utcNow;
        LatestInspectedByUserId = actorUserId;

        if (result is ContainerInspectionResult.Failed)
        {
            RevokeRelease(actorUserId, utcNow);
        }

        var entries = new List<ContainerControlEvent>
        {
            ContainerControlEvent.Record(
                Id,
                ContainerControlAction.InspectionRecorded,
                fromState,
                State,
                actorUserId,
                utcNow,
                inspectionResult: result,
                temperatureCelsius: temperatureCelsius,
                notes: normalizedNotes),
        };

        if (result is ContainerInspectionResult.Passed
            && State is ContainerControlState.Held)
        {
            var heldReason = StateReason;

            State = ContainerControlState.Pending;
            StateReason = null;
            StateChangedByUserId = actorUserId;
            StateChangedAtUtc = utcNow;

            entries.Add(ContainerControlEvent.Record(
                Id,
                ContainerControlAction.HoldLifted,
                ContainerControlState.Held,
                State,
                actorUserId,
                utcNow,
                notes: heldReason));
        }

        Touch();

        return entries;
    }

    internal ContainerControlEvent Release(Guid actorUserId, DateTime utcNow, string? notes)
    {
        if (ReleaseBlocker is { } blocker)
        {
            throw new ContainerControlTransitionNotAllowedException(
                Id,
                State,
                ContainerControlTransitionNotAllowedException.ReleaseAction,
                blocker);
        }

        var fromState = State;
        var normalizedNotes = NormalizeOptional(notes);

        State = ContainerControlState.Released;
        StateReason = normalizedNotes;
        StateChangedByUserId = actorUserId;
        StateChangedAtUtc = utcNow;
        Touch();

        return ContainerControlEvent.Record(
            Id,
            ContainerControlAction.Released,
            fromState,
            State,
            actorUserId,
            utcNow,
            sealNumber: SealNumber,
            notes: normalizedNotes);
    }

    internal ContainerControlEvent Hold(string reason, Guid actorUserId, DateTime utcNow)
    {
        if (State is ContainerControlState.Held)
        {
            throw new ContainerControlTransitionNotAllowedException(
                Id,
                State,
                ContainerControlTransitionNotAllowedException.HoldAction,
                ContainerControlTransitionNotAllowedException.AlreadyHeldReason);
        }

        var normalizedReason = NormalizeOptional(reason)
            ?? throw new ContainerHoldReasonRequiredException(Id);

        var fromState = State;

        State = ContainerControlState.Held;
        StateReason = normalizedReason;
        StateChangedByUserId = actorUserId;
        StateChangedAtUtc = utcNow;
        Touch();

        return ContainerControlEvent.Record(
            Id,
            ContainerControlAction.Held,
            fromState,
            State,
            actorUserId,
            utcNow,
            notes: normalizedReason);
    }

    private void RevokeRelease(Guid actorUserId, DateTime utcNow)
    {
        if (State is not ContainerControlState.Released)
        {
            return;
        }

        State = ContainerControlState.Pending;
        StateReason = null;
        StateChangedByUserId = actorUserId;
        StateChangedAtUtc = utcNow;
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
