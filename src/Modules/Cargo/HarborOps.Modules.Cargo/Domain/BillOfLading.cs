namespace HarborOps.Modules.Cargo.Domain;

internal sealed class BillOfLading
{
    internal const int BillNumberMaxLength = 30;
    internal const int PartyNameMaxLength = 200;
    internal const int PartyAddressMaxLength = 500;
    internal const int LocationMaxLength = 200;
    internal const int CargoMarksMaxLength = 500;
    internal const int CargoDescriptionMaxLength = 1000;
    internal const int VoidReasonMaxLength = 500;
    internal const int FreightTermsMaxLength = 50;
    internal const int StatusMaxLength = 50;
    internal const int BookingNumberMaxLength = 50;
    internal const int CargoTypeMaxLength = 100;
    internal const int BookingDescriptionMaxLength = 500;
    internal const int WeightKgPrecision = 18;
    internal const int WeightKgScale = 3;

    internal const CargoStatus MinimumIssueBookingStatus = CargoStatus.Loaded;

    private BillOfLading()
    {
    }

    private BillOfLading(
        Guid id,
        Guid cargoBookingId,
        BillOfLadingDraftValues values,
        DateTime utcNow)
    {
        Id = id;
        CargoBookingId = cargoBookingId;
        Status = BillOfLadingStatus.Draft;
        CreatedUtc = utcNow;
        ConcurrencyToken = Guid.NewGuid();
        Apply(values);
    }

    public Guid Id { get; private set; }

    public Guid CargoBookingId { get; private set; }

    public string BillNumber { get; private set; } = null!;

    public BillOfLadingStatus Status { get; private set; }

    public string ShipperName { get; private set; } = null!;

    public string ShipperAddress { get; private set; } = null!;

    public string ConsigneeName { get; private set; } = null!;

    public string ConsigneeAddress { get; private set; } = null!;

    public string? NotifyPartyName { get; private set; }

    public string? NotifyPartyAddress { get; private set; }

    public string PlaceOfReceipt { get; private set; } = null!;

    public string PortOfLoading { get; private set; } = null!;

    public string PortOfDischarge { get; private set; } = null!;

    public string PlaceOfDelivery { get; private set; } = null!;

    public FreightTerms FreightTerms { get; private set; }

    public string CargoMarks { get; private set; } = null!;

    public string CargoDescription { get; private set; } = null!;

    public int PackageCount { get; private set; }

    public decimal WeightKg { get; private set; }

    public string IssuePlace { get; private set; } = null!;

    public DateOnly RequestedIssueDate { get; private set; }

    public Guid? IssuedByUserId { get; private set; }

    public DateTime? IssuedAtUtc { get; private set; }

    public Guid? VoidedByUserId { get; private set; }

    public DateTime? VoidedAtUtc { get; private set; }

    public string? VoidReason { get; private set; }

    public string? SnapshotBookingNumber { get; private set; }

    public Guid? SnapshotVoyageId { get; private set; }

    public string? SnapshotCargoType { get; private set; }

    public string? SnapshotBookingDescription { get; private set; }

    public int? SnapshotBookingPackageCount { get; private set; }

    public decimal? SnapshotBookingWeightKg { get; private set; }

    public CargoStatus? SnapshotBookingStatus { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    internal bool IsEditable => Status is BillOfLadingStatus.Draft;

    internal bool BlocksContainerSelection => Status is not BillOfLadingStatus.Voided;

    private bool HasRequiredDetails =>
        !string.IsNullOrWhiteSpace(BillNumber)
        && !string.IsNullOrWhiteSpace(ShipperName)
        && !string.IsNullOrWhiteSpace(ShipperAddress)
        && !string.IsNullOrWhiteSpace(ConsigneeName)
        && !string.IsNullOrWhiteSpace(ConsigneeAddress)
        && !string.IsNullOrWhiteSpace(PlaceOfReceipt)
        && !string.IsNullOrWhiteSpace(PortOfLoading)
        && !string.IsNullOrWhiteSpace(PortOfDischarge)
        && !string.IsNullOrWhiteSpace(PlaceOfDelivery)
        && !string.IsNullOrWhiteSpace(CargoMarks)
        && !string.IsNullOrWhiteSpace(CargoDescription)
        && !string.IsNullOrWhiteSpace(IssuePlace);

    internal static string NormalizeBillNumber(string billNumber)
    {
        ArgumentNullException.ThrowIfNull(billNumber);

        return string.Concat(billNumber.Where(character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant();
    }

    internal static BillOfLading Draft(
        Guid cargoBookingId,
        BillOfLadingDraftValues values,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(values);

        return new BillOfLading(Guid.NewGuid(), cargoBookingId, values, utcNow);
    }

    internal string? IssueBlocker(CargoStatus bookingStatus, int selectedContainerCount)
    {
        return Status switch
        {
            BillOfLadingStatus.Issued =>
                BillOfLadingTransitionNotAllowedException.AlreadyIssuedReason,
            BillOfLadingStatus.Voided =>
                BillOfLadingTransitionNotAllowedException.AlreadyVoidedReason,
            _ when selectedContainerCount <= 0 =>
                BillOfLadingTransitionNotAllowedException.NoContainersReason,
            _ when !HasRequiredDetails =>
                BillOfLadingTransitionNotAllowedException.IncompleteReason,
            _ when bookingStatus < MinimumIssueBookingStatus =>
                BillOfLadingTransitionNotAllowedException.BookingNotLoadedReason,
            _ => null,
        };
    }

    internal BillOfLadingEvent UpdateDraft(
        BillOfLadingDraftValues values,
        Guid actorUserId,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (!IsEditable)
        {
            throw new BillOfLadingTransitionNotAllowedException(
                Id,
                Status,
                BillOfLadingTransitionNotAllowedException.UpdateAction,
                Status is BillOfLadingStatus.Issued
                    ? BillOfLadingTransitionNotAllowedException.AlreadyIssuedReason
                    : BillOfLadingTransitionNotAllowedException.AlreadyVoidedReason);
        }

        Apply(values);
        Touch();

        return BillOfLadingEvent.Record(
            Id,
            BillOfLadingAction.Updated,
            Status,
            Status,
            actorUserId,
            utcNow);
    }

    internal BillOfLadingEvent Issue(
        BillOfLadingBookingSnapshot booking,
        int selectedContainerCount,
        Guid actorUserId,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(booking);

        if (IssueBlocker(booking.Status, selectedContainerCount) is { } blocker)
        {
            throw new BillOfLadingTransitionNotAllowedException(
                Id,
                Status,
                BillOfLadingTransitionNotAllowedException.IssueAction,
                blocker);
        }

        var fromStatus = Status;

        Status = BillOfLadingStatus.Issued;
        IssuedByUserId = actorUserId;
        IssuedAtUtc = utcNow;
        SnapshotBookingNumber = booking.BookingNumber;
        SnapshotVoyageId = booking.VoyageId;
        SnapshotCargoType = booking.CargoType;
        SnapshotBookingDescription = booking.Description;
        SnapshotBookingPackageCount = booking.PackageCount;
        SnapshotBookingWeightKg = booking.WeightKg;
        SnapshotBookingStatus = booking.Status;
        Touch();

        return BillOfLadingEvent.Record(
            Id,
            BillOfLadingAction.Issued,
            fromStatus,
            Status,
            actorUserId,
            utcNow);
    }

    internal BillOfLadingEvent Void(string reason, Guid actorUserId, DateTime utcNow)
    {
        if (Status is not BillOfLadingStatus.Issued)
        {
            throw new BillOfLadingTransitionNotAllowedException(
                Id,
                Status,
                BillOfLadingTransitionNotAllowedException.VoidAction,
                Status is BillOfLadingStatus.Voided
                    ? BillOfLadingTransitionNotAllowedException.AlreadyVoidedReason
                    : BillOfLadingTransitionNotAllowedException.StillDraftReason);
        }

        var normalizedReason = NormalizeOptional(reason)
            ?? throw new BillOfLadingVoidReasonRequiredException(Id);

        var fromStatus = Status;

        Status = BillOfLadingStatus.Voided;
        VoidedByUserId = actorUserId;
        VoidedAtUtc = utcNow;
        VoidReason = normalizedReason;
        Touch();

        return BillOfLadingEvent.Record(
            Id,
            BillOfLadingAction.Voided,
            fromStatus,
            Status,
            actorUserId,
            utcNow,
            normalizedReason);
    }

    private void Apply(BillOfLadingDraftValues values)
    {
        BillNumber = NormalizeBillNumber(values.BillNumber);
        ShipperName = Normalize(values.ShipperName);
        ShipperAddress = Normalize(values.ShipperAddress);
        ConsigneeName = Normalize(values.ConsigneeName);
        ConsigneeAddress = Normalize(values.ConsigneeAddress);
        NotifyPartyName = NormalizeOptional(values.NotifyPartyName);
        NotifyPartyAddress = NormalizeOptional(values.NotifyPartyAddress);
        PlaceOfReceipt = Normalize(values.PlaceOfReceipt);
        PortOfLoading = Normalize(values.PortOfLoading);
        PortOfDischarge = Normalize(values.PortOfDischarge);
        PlaceOfDelivery = Normalize(values.PlaceOfDelivery);
        FreightTerms = values.FreightTerms;
        CargoMarks = Normalize(values.CargoMarks);
        CargoDescription = Normalize(values.CargoDescription);
        PackageCount = values.PackageCount;
        WeightKg = values.WeightKg;
        IssuePlace = Normalize(values.IssuePlace);
        RequestedIssueDate = values.RequestedIssueDate;
    }

    private void Touch()
    {
        ConcurrencyToken = Guid.NewGuid();
    }

    private static string Normalize(string value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();

        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
