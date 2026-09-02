namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoSafetyInfo
{
    internal const int HandlingNotesMaxLength = 1000;
    internal const int ImdgClassMaxLength = 10;
    internal const int ReviewNotesMaxLength = 1000;
    internal const int ReviewStatusMaxLength = 50;
    internal const int StorageConditionsMaxLength = 500;
    internal const int UnNumberMaxLength = 10;

    private CargoSafetyInfo()
    {
    }

    private CargoSafetyInfo(
        Guid id,
        Guid cargoBookingId,
        string imdgClass,
        string unNumber,
        string storageConditions,
        string? handlingNotes)
    {
        Id = id;
        CargoBookingId = cargoBookingId;
        ImdgClass = imdgClass;
        UnNumber = unNumber;
        StorageConditions = storageConditions;
        HandlingNotes = handlingNotes;
        ReviewStatus = CargoSafetyReviewStatus.Draft;
    }

    public Guid Id { get; private set; }

    public Guid CargoBookingId { get; private set; }

    public string ImdgClass { get; private set; } = null!;

    public string UnNumber { get; private set; } = null!;

    public string StorageConditions { get; private set; } = null!;

    public string? HandlingNotes { get; private set; }

    public CargoSafetyReviewStatus ReviewStatus { get; private set; }

    public Guid? SubmittedByUserId { get; private set; }

    public DateTime? SubmittedAtUtc { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTime? ReviewedAtUtc { get; private set; }

    public string? ReviewNotes { get; private set; }

    internal bool IsEditable =>
        ReviewStatus is CargoSafetyReviewStatus.Draft or CargoSafetyReviewStatus.Rejected;

    internal static bool AllowsLoading(CargoSafetyReviewStatus? reviewStatus)
    {
        return reviewStatus is CargoSafetyReviewStatus.Approved;
    }

    internal static CargoSafetyInfo Declare(
        Guid cargoBookingId,
        string imdgClass,
        string unNumber,
        string storageConditions,
        string? handlingNotes)
    {
        return new CargoSafetyInfo(
            Guid.NewGuid(),
            cargoBookingId,
            imdgClass,
            unNumber,
            storageConditions.Trim(),
            NormalizeOptional(handlingNotes));
    }

    internal void Update(
        string imdgClass,
        string unNumber,
        string storageConditions,
        string? handlingNotes)
    {
        if (!IsEditable)
        {
            throw new CargoSafetyInfoTransitionNotAllowedException(
                Id,
                ReviewStatus,
                CargoSafetyInfoTransitionNotAllowedException.EditAction);
        }

        ImdgClass = imdgClass;
        UnNumber = unNumber;
        StorageConditions = storageConditions.Trim();
        HandlingNotes = NormalizeOptional(handlingNotes);
    }

    internal CargoSafetyReviewEntry Submit(Guid submittedByUserId, DateTime utcNow)
    {
        if (!IsEditable)
        {
            throw new CargoSafetyInfoTransitionNotAllowedException(
                Id,
                ReviewStatus,
                CargoSafetyInfoTransitionNotAllowedException.SubmitAction);
        }

        var fromStatus = ReviewStatus;

        ReviewStatus = CargoSafetyReviewStatus.Submitted;
        SubmittedByUserId = submittedByUserId;
        SubmittedAtUtc = utcNow;
        ReviewedByUserId = null;
        ReviewedAtUtc = null;
        ReviewNotes = null;

        return CargoSafetyReviewEntry.Record(
            Id,
            fromStatus,
            ReviewStatus,
            submittedByUserId,
            utcNow,
            null);
    }

    internal CargoSafetyReviewEntry Approve(Guid reviewedByUserId, DateTime utcNow, string? notes)
    {
        return CompleteReview(CargoSafetyReviewStatus.Approved, reviewedByUserId, utcNow, notes);
    }

    internal CargoSafetyReviewEntry Reject(Guid reviewedByUserId, DateTime utcNow, string? notes)
    {
        return CompleteReview(CargoSafetyReviewStatus.Rejected, reviewedByUserId, utcNow, notes);
    }

    private CargoSafetyReviewEntry CompleteReview(
        CargoSafetyReviewStatus decision,
        Guid reviewedByUserId,
        DateTime utcNow,
        string? notes)
    {
        if (ReviewStatus is not CargoSafetyReviewStatus.Submitted)
        {
            throw new CargoSafetyInfoTransitionNotAllowedException(
                Id,
                ReviewStatus,
                CargoSafetyInfoTransitionNotAllowedException.ReviewAction);
        }

        var normalizedNotes = NormalizeOptional(notes);

        if (decision is CargoSafetyReviewStatus.Rejected && normalizedNotes is null)
        {
            throw new CargoSafetyReviewNotesRequiredException(Id);
        }

        var fromStatus = ReviewStatus;

        ReviewStatus = decision;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAtUtc = utcNow;
        ReviewNotes = normalizedNotes;

        return CargoSafetyReviewEntry.Record(
            Id,
            fromStatus,
            decision,
            reviewedByUserId,
            utcNow,
            normalizedNotes);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();

        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
