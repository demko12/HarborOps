using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.UnitTests.Cargo;

public sealed class CargoSafetyInfoTests
{
    private static readonly Guid CargoBookingId =
        Guid.Parse("6b5a4c3d-2e1f-4a9b-8c7d-6e5f4a3b2c1d");

    private static readonly Guid OfficerUserId =
        Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    private static readonly Guid ReviewerUserId =
        Guid.Parse("9f8e7d6c-5b4a-4938-8271-6a5b4c3d2e1f");

    private static readonly DateTime SubmittedAtUtc =
        new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime ReviewedAtUtc =
        new(2026, 9, 1, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Declare_starts_the_declaration_as_a_draft()
    {
        var declaration = Declare();

        Assert.Equal(CargoSafetyReviewStatus.Draft, declaration.ReviewStatus);
        Assert.Null(declaration.SubmittedByUserId);
        Assert.Null(declaration.SubmittedAtUtc);
        Assert.Null(declaration.ReviewedByUserId);
        Assert.Null(declaration.ReviewedAtUtc);
        Assert.Null(declaration.ReviewNotes);
    }

    [Fact]
    public void Declare_trims_storage_conditions_and_handling_notes()
    {
        var declaration = CargoSafetyInfo.Declare(
            CargoBookingId,
            "3",
            "UN1203",
            "  Keep below 25C  ",
            "  Away from heat  ");

        Assert.Equal("Keep below 25C", declaration.StorageConditions);
        Assert.Equal("Away from heat", declaration.HandlingNotes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Declare_treats_blank_handling_notes_as_absent(string? handlingNotes)
    {
        var declaration = CargoSafetyInfo.Declare(
            CargoBookingId,
            "3",
            "UN1203",
            "Keep below 25C",
            handlingNotes);

        Assert.Null(declaration.HandlingNotes);
    }

    [Fact]
    public void Update_replaces_the_details_of_a_draft()
    {
        var declaration = Declare();

        declaration.Update("2.1", "UN1950", "  Ventilated hold  ", "  Upright only  ");

        Assert.Equal("2.1", declaration.ImdgClass);
        Assert.Equal("UN1950", declaration.UnNumber);
        Assert.Equal("Ventilated hold", declaration.StorageConditions);
        Assert.Equal("Upright only", declaration.HandlingNotes);
        Assert.Equal(CargoSafetyReviewStatus.Draft, declaration.ReviewStatus);
    }

    [Fact]
    public void Update_replaces_the_details_of_a_rejected_declaration()
    {
        var declaration = Declare();
        declaration.Submit(OfficerUserId, SubmittedAtUtc);
        declaration.Reject(ReviewerUserId, ReviewedAtUtc, "UN number does not match the class.");

        declaration.Update("2.1", "UN1950", "Ventilated hold", handlingNotes: null);

        Assert.Equal("UN1950", declaration.UnNumber);
        Assert.Equal(CargoSafetyReviewStatus.Rejected, declaration.ReviewStatus);
    }

    [Fact]
    public void Update_is_rejected_while_the_declaration_awaits_review()
    {
        var declaration = Declare();
        declaration.Submit(OfficerUserId, SubmittedAtUtc);

        var exception = Assert.Throws<CargoSafetyInfoTransitionNotAllowedException>(
            () => declaration.Update("2.1", "UN1950", "Ventilated hold", handlingNotes: null));

        Assert.Equal(declaration.Id, exception.CargoSafetyInfoId);
        Assert.Equal(CargoSafetyReviewStatus.Submitted, exception.CurrentStatus);
        Assert.Equal(CargoSafetyInfoTransitionNotAllowedException.EditAction, exception.RequestedAction);
    }

    [Fact]
    public void Update_is_rejected_once_the_declaration_is_approved()
    {
        var declaration = Approved();

        var exception = Assert.Throws<CargoSafetyInfoTransitionNotAllowedException>(
            () => declaration.Update("2.1", "UN1950", "Ventilated hold", handlingNotes: null));

        Assert.Equal(CargoSafetyReviewStatus.Approved, exception.CurrentStatus);
        Assert.Equal("UN1203", declaration.UnNumber);
    }

    [Fact]
    public void Submit_moves_a_draft_into_review_and_records_the_actor_and_time()
    {
        var declaration = Declare();

        var entry = declaration.Submit(OfficerUserId, SubmittedAtUtc);

        Assert.Equal(CargoSafetyReviewStatus.Submitted, declaration.ReviewStatus);
        Assert.Equal(OfficerUserId, declaration.SubmittedByUserId);
        Assert.Equal(SubmittedAtUtc, declaration.SubmittedAtUtc);
        Assert.Equal(declaration.Id, entry.CargoSafetyInfoId);
        Assert.Equal(CargoSafetyReviewStatus.Draft, entry.FromStatus);
        Assert.Equal(CargoSafetyReviewStatus.Submitted, entry.ToStatus);
        Assert.Equal(OfficerUserId, entry.ActorUserId);
        Assert.Equal(SubmittedAtUtc, entry.OccurredUtc);
        Assert.Null(entry.Notes);
    }

    [Fact]
    public void Submit_clears_the_outcome_of_the_previous_review()
    {
        var declaration = Declare();
        declaration.Submit(OfficerUserId, SubmittedAtUtc);
        declaration.Reject(ReviewerUserId, ReviewedAtUtc, "UN number does not match the class.");

        var entry = declaration.Submit(OfficerUserId, ReviewedAtUtc.AddHours(1));

        Assert.Equal(CargoSafetyReviewStatus.Submitted, declaration.ReviewStatus);
        Assert.Null(declaration.ReviewedByUserId);
        Assert.Null(declaration.ReviewedAtUtc);
        Assert.Null(declaration.ReviewNotes);
        Assert.Equal(CargoSafetyReviewStatus.Rejected, entry.FromStatus);
    }

    [Fact]
    public void Submit_is_rejected_while_the_declaration_already_awaits_review()
    {
        var declaration = Declare();
        declaration.Submit(OfficerUserId, SubmittedAtUtc);

        var exception = Assert.Throws<CargoSafetyInfoTransitionNotAllowedException>(
            () => declaration.Submit(OfficerUserId, SubmittedAtUtc.AddHours(1)));

        Assert.Equal(CargoSafetyReviewStatus.Submitted, exception.CurrentStatus);
        Assert.Equal(CargoSafetyInfoTransitionNotAllowedException.SubmitAction, exception.RequestedAction);
    }

    [Fact]
    public void Submit_is_rejected_once_the_declaration_is_approved()
    {
        var declaration = Approved();

        Assert.Throws<CargoSafetyInfoTransitionNotAllowedException>(
            () => declaration.Submit(OfficerUserId, ReviewedAtUtc.AddHours(1)));
    }

    [Fact]
    public void Approve_records_the_reviewer_the_time_and_a_history_entry()
    {
        var declaration = Declare();
        declaration.Submit(OfficerUserId, SubmittedAtUtc);

        var entry = declaration.Approve(ReviewerUserId, ReviewedAtUtc, "Checked against the manifest.");

        Assert.Equal(CargoSafetyReviewStatus.Approved, declaration.ReviewStatus);
        Assert.Equal(ReviewerUserId, declaration.ReviewedByUserId);
        Assert.Equal(ReviewedAtUtc, declaration.ReviewedAtUtc);
        Assert.Equal("Checked against the manifest.", declaration.ReviewNotes);
        Assert.Equal(CargoSafetyReviewStatus.Submitted, entry.FromStatus);
        Assert.Equal(CargoSafetyReviewStatus.Approved, entry.ToStatus);
        Assert.Equal(ReviewerUserId, entry.ActorUserId);
        Assert.Equal(ReviewedAtUtc, entry.OccurredUtc);
        Assert.Equal("Checked against the manifest.", entry.Notes);
    }

    [Fact]
    public void Approve_does_not_require_review_notes()
    {
        var declaration = Declare();
        declaration.Submit(OfficerUserId, SubmittedAtUtc);

        var entry = declaration.Approve(ReviewerUserId, ReviewedAtUtc, notes: null);

        Assert.Equal(CargoSafetyReviewStatus.Approved, declaration.ReviewStatus);
        Assert.Null(declaration.ReviewNotes);
        Assert.Null(entry.Notes);
    }

    [Fact]
    public void Reject_records_the_reason_on_the_declaration_and_in_the_history()
    {
        var declaration = Declare();
        declaration.Submit(OfficerUserId, SubmittedAtUtc);

        var entry = declaration.Reject(ReviewerUserId, ReviewedAtUtc, "  Storage conditions are unclear.  ");

        Assert.Equal(CargoSafetyReviewStatus.Rejected, declaration.ReviewStatus);
        Assert.Equal(ReviewerUserId, declaration.ReviewedByUserId);
        Assert.Equal(ReviewedAtUtc, declaration.ReviewedAtUtc);
        Assert.Equal("Storage conditions are unclear.", declaration.ReviewNotes);
        Assert.Equal("Storage conditions are unclear.", entry.Notes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_requires_review_notes(string? notes)
    {
        var declaration = Declare();
        declaration.Submit(OfficerUserId, SubmittedAtUtc);

        var exception = Assert.Throws<CargoSafetyReviewNotesRequiredException>(
            () => declaration.Reject(ReviewerUserId, ReviewedAtUtc, notes));

        Assert.Equal(declaration.Id, exception.CargoSafetyInfoId);
        Assert.Equal(CargoSafetyReviewStatus.Submitted, declaration.ReviewStatus);
        Assert.Null(declaration.ReviewedByUserId);
    }

    [Fact]
    public void Approve_is_rejected_while_the_declaration_is_still_a_draft()
    {
        var declaration = Declare();

        var exception = Assert.Throws<CargoSafetyInfoTransitionNotAllowedException>(
            () => declaration.Approve(ReviewerUserId, ReviewedAtUtc, notes: null));

        Assert.Equal(CargoSafetyReviewStatus.Draft, exception.CurrentStatus);
        Assert.Equal(CargoSafetyInfoTransitionNotAllowedException.ReviewAction, exception.RequestedAction);
    }

    [Fact]
    public void Approve_is_rejected_once_the_declaration_is_already_reviewed()
    {
        var declaration = Approved();

        Assert.Throws<CargoSafetyInfoTransitionNotAllowedException>(
            () => declaration.Approve(ReviewerUserId, ReviewedAtUtc.AddHours(1), notes: null));
    }

    [Fact]
    public void Reject_is_rejected_while_the_declaration_is_still_a_draft()
    {
        var declaration = Declare();

        Assert.Throws<CargoSafetyInfoTransitionNotAllowedException>(
            () => declaration.Reject(ReviewerUserId, ReviewedAtUtc, "Not reviewable yet."));
    }

    [Fact]
    public void AllowsLoading_accepts_only_an_approved_declaration()
    {
        Assert.True(CargoSafetyInfo.AllowsLoading(CargoSafetyReviewStatus.Approved));
        Assert.False(CargoSafetyInfo.AllowsLoading(CargoSafetyReviewStatus.Draft));
        Assert.False(CargoSafetyInfo.AllowsLoading(CargoSafetyReviewStatus.Submitted));
        Assert.False(CargoSafetyInfo.AllowsLoading(CargoSafetyReviewStatus.Rejected));
        Assert.False(CargoSafetyInfo.AllowsLoading(reviewStatus: null));
    }

    private static CargoSafetyInfo Declare()
    {
        return CargoSafetyInfo.Declare(
            CargoBookingId,
            "3",
            "UN1203",
            "Keep below 25C",
            handlingNotes: null);
    }

    private static CargoSafetyInfo Approved()
    {
        var declaration = Declare();

        declaration.Submit(OfficerUserId, SubmittedAtUtc);
        declaration.Approve(ReviewerUserId, ReviewedAtUtc, notes: null);

        return declaration;
    }
}
