using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class CrewAvailabilityRequestTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_starts_in_the_requested_status()
    {
        var request = CreateRequest();

        Assert.Equal(CrewAvailabilityRequest.RequestedStatus, request.Status);
        Assert.Equal(Now, request.RequestedAtUtc);
        Assert.Null(request.ReviewedByUserId);
    }

    [Fact]
    public void Approve_moves_to_approved_and_records_the_reviewer()
    {
        var request = CreateRequest();
        var reviewer = Guid.NewGuid();
        var reviewedAt = Now.AddDays(1);

        request.Approve(reviewer, reviewedAt);

        Assert.Equal(CrewAvailabilityRequest.ApprovedStatus, request.Status);
        Assert.Equal(reviewer, request.ReviewedByUserId);
        Assert.Equal(reviewedAt, request.ReviewedAtUtc);
    }

    [Fact]
    public void Approve_throws_once_already_approved()
    {
        var request = CreateRequest();
        request.Approve(Guid.NewGuid(), Now);

        var exception = Assert.Throws<InvalidCrewAvailabilityTransitionException>(
            () => request.Approve(Guid.NewGuid(), Now));

        Assert.Equal(CrewAvailabilityRequest.ApproveAction, exception.AttemptedAction);
    }

    [Fact]
    public void Reject_requires_moving_from_requested_and_stores_the_reason()
    {
        var request = CreateRequest();

        request.Reject(Guid.NewGuid(), "Operational cover unavailable.", Now);

        Assert.Equal(CrewAvailabilityRequest.RejectedStatus, request.Status);
        Assert.Equal("Operational cover unavailable.", request.DecisionReason);
    }

    [Fact]
    public void Reject_throws_after_approval()
    {
        var request = CreateRequest();
        request.Approve(Guid.NewGuid(), Now);

        Assert.Throws<InvalidCrewAvailabilityTransitionException>(
            () => request.Reject(Guid.NewGuid(), "Too late.", Now));
    }

    [Fact]
    public void Cancel_is_allowed_from_requested()
    {
        var request = CreateRequest();

        request.Cancel(Guid.NewGuid(), "No longer needed.", Now, DateOnly.FromDateTime(Now));

        Assert.Equal(CrewAvailabilityRequest.CancelledStatus, request.Status);
        Assert.Equal("No longer needed.", request.DecisionReason);
    }

    [Fact]
    public void Cancel_is_allowed_for_an_approved_period_that_has_not_started()
    {
        var request = CreateRequest(startDate: new DateOnly(2026, 7, 1));
        request.Approve(Guid.NewGuid(), Now);

        request.Cancel(Guid.NewGuid(), "Plans changed.", Now, new DateOnly(2026, 6, 20));

        Assert.Equal(CrewAvailabilityRequest.CancelledStatus, request.Status);
    }

    [Fact]
    public void Cancel_throws_for_an_approved_period_that_has_already_started()
    {
        var request = CreateRequest(startDate: new DateOnly(2026, 6, 1));
        request.Approve(Guid.NewGuid(), Now);

        Assert.Throws<InvalidCrewAvailabilityTransitionException>(
            () => request.Cancel(Guid.NewGuid(), "Too late.", Now, new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Cancel_throws_once_rejected()
    {
        var request = CreateRequest();
        request.Reject(Guid.NewGuid(), "Denied.", Now);

        Assert.Throws<InvalidCrewAvailabilityTransitionException>(
            () => request.Cancel(Guid.NewGuid(), "Anything.", Now, DateOnly.FromDateTime(Now)));
    }

    private static CrewAvailabilityRequest CreateRequest(DateOnly? startDate = null)
    {
        var start = startDate ?? new DateOnly(2026, 8, 1);

        return CrewAvailabilityRequest.Create(
            Guid.NewGuid(),
            "AnnualLeave",
            start,
            start.AddDays(10),
            "Family holiday.",
            notes: null,
            requestedByUserId: Guid.NewGuid(),
            requestedAtUtc: Now);
    }
}
