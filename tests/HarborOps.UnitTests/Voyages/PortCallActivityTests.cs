using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.UnitTests.Voyages;

public sealed class PortCallActivityTests
{
    private static readonly Guid ActorId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string ActorName = "master@harborops.test";

    private static readonly DateTime OccurredAtUtc = new(2026, 9, 1, 11, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime RecordedAtUtc = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Recording_an_activity_starts_it_active_at_revision_one_with_a_recorded_history_entry()
    {
        var (activity, revision) = Record();

        Assert.Equal(PortCallActivity.ActiveStatus, activity.Status);
        Assert.Equal(1, activity.Revision);
        Assert.False(activity.IsLocked);
        Assert.NotEqual(Guid.Empty, activity.ConcurrencyToken);
        Assert.Equal(ActorId, activity.RecordedByUserId);
        Assert.Equal(RecordedAtUtc, activity.RecordedAtUtc);

        Assert.Equal(activity.Id, revision.PortCallActivityId);
        Assert.Equal(1, revision.Revision);
        Assert.Equal(PortCallActivityRevision.RecordedChangeType, revision.ChangeType);
        Assert.Equal(PortCallActivityType.AllFast, revision.ActivityType);
        Assert.Equal(OccurredAtUtc, revision.OccurredAtUtc);
        Assert.Null(revision.Reason);
    }

    [Fact]
    public void An_activity_cannot_end_before_it_starts()
    {
        Assert.Throws<PortCallActivityChronologyException>(() => PortCallActivity.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            PortCallActivityType.CargoStarted,
            OccurredAtUtc,
            OccurredAtUtc.AddMinutes(-1),
            null,
            ActorId,
            ActorName,
            RecordedAtUtc));
    }

    [Fact]
    public void An_activity_may_end_at_the_same_instant_it_starts()
    {
        var (activity, _) = PortCallActivity.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            PortCallActivityType.OperationalNote,
            OccurredAtUtc,
            OccurredAtUtc,
            null,
            ActorId,
            ActorName,
            RecordedAtUtc);

        Assert.Equal(activity.OccurredAtUtc, activity.EndedAtUtc);
    }

    [Fact]
    public void A_correction_changes_the_current_values_and_keeps_the_original_in_history()
    {
        var (activity, original) = Record();
        var firstToken = activity.ConcurrencyToken;
        var correctedAtUtc = RecordedAtUtc.AddHours(1);

        var correction = activity.Correct(
            PortCallActivityType.PilotOnBoard,
            OccurredAtUtc.AddMinutes(-30),
            OccurredAtUtc,
            "Pilot boarded at the breakwater",
            "Wrong event type",
            ActorId,
            ActorName,
            correctedAtUtc);

        Assert.Equal(PortCallActivityType.PilotOnBoard, activity.ActivityType);
        Assert.Equal(OccurredAtUtc.AddMinutes(-30), activity.OccurredAtUtc);
        Assert.Equal(2, activity.Revision);
        Assert.NotEqual(firstToken, activity.ConcurrencyToken);
        Assert.Equal(correctedAtUtc, activity.LastChangedAtUtc);

        Assert.Equal(PortCallActivityType.AllFast, original.ActivityType);
        Assert.Equal(OccurredAtUtc, original.OccurredAtUtc);

        Assert.Equal(2, correction.Revision);
        Assert.Equal(PortCallActivityRevision.CorrectedChangeType, correction.ChangeType);
        Assert.Equal(PortCallActivityType.PilotOnBoard, correction.ActivityType);
        Assert.Equal("Wrong event type", correction.Reason);
        Assert.Equal(correctedAtUtc, correction.ChangedAtUtc);
    }

    [Fact]
    public void Voiding_keeps_the_values_and_records_the_reason()
    {
        var (activity, _) = Record();

        var voided = activity.Void("Duplicate entry", ActorId, ActorName, RecordedAtUtc.AddHours(1));

        Assert.Equal(PortCallActivity.VoidedStatus, activity.Status);
        Assert.False(activity.IsActive);
        Assert.Equal(2, activity.Revision);
        Assert.Equal(PortCallActivityRevision.VoidedChangeType, voided.ChangeType);
        Assert.Equal(PortCallActivityType.AllFast, voided.ActivityType);
        Assert.Equal("Duplicate entry", voided.Reason);
    }

    [Fact]
    public void A_voided_activity_can_no_longer_be_corrected_or_voided()
    {
        var (activity, _) = Record();
        activity.Void("Duplicate entry", ActorId, ActorName, RecordedAtUtc);

        Assert.Throws<PortCallActivityVoidedException>(() => activity.Correct(
            PortCallActivityType.AllFast,
            OccurredAtUtc,
            null,
            null,
            "Restore",
            ActorId,
            ActorName,
            RecordedAtUtc));
        Assert.Throws<PortCallActivityVoidedException>(() =>
            activity.Void("Again", ActorId, ActorName, RecordedAtUtc));
    }

    [Fact]
    public void A_locked_activity_is_immutable_and_locking_changes_its_version()
    {
        var (activity, _) = Record();
        var tokenBeforeLock = activity.ConcurrencyToken;

        activity.Lock();

        Assert.True(activity.IsLocked);
        Assert.NotEqual(tokenBeforeLock, activity.ConcurrencyToken);
        Assert.Throws<PortCallActivityLockedException>(() => activity.Correct(
            PortCallActivityType.AllFast,
            OccurredAtUtc,
            null,
            null,
            "Late fix",
            ActorId,
            ActorName,
            RecordedAtUtc));
        Assert.Throws<PortCallActivityLockedException>(() =>
            activity.Void("Late void", ActorId, ActorName, RecordedAtUtc));
        Assert.Throws<PortCallActivityLockedException>(activity.Lock);
    }

    [Theory]
    [InlineData(-1, null, true)]
    [InlineData(0, null, false)]
    [InlineData(0, 480, false)]
    [InlineData(60, 481, true)]
    [InlineData(481, null, true)]
    public void An_activity_is_outside_the_port_call_when_it_starts_before_arrival_or_ends_after_departure(
        int startMinutesAfterArrival,
        int? endMinutesAfterArrival,
        bool expected)
    {
        var arrival = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var departure = arrival.AddHours(8);

        var (activity, _) = PortCallActivity.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            PortCallActivityType.CargoStarted,
            arrival.AddMinutes(startMinutesAfterArrival),
            endMinutesAfterArrival is { } end ? arrival.AddMinutes(end) : null,
            null,
            ActorId,
            ActorName,
            RecordedAtUtc);

        Assert.Equal(expected, activity.IsOutsidePortCall(arrival, departure));
    }

    [Fact]
    public void Only_the_bounded_activity_types_are_valid()
    {
        Assert.Equal(
            ["PilotOnBoard", "AllFast", "CargoStarted", "CargoCompleted", "OperationalNote"],
            PortCallActivityType.All);
        Assert.False(PortCallActivityType.IsValid("allfast"));
        Assert.False(PortCallActivityType.IsValid("Laytime"));
        Assert.False(PortCallActivityType.IsValid(null));
    }

    private static (PortCallActivity Activity, PortCallActivityRevision Revision) Record() =>
        PortCallActivity.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            PortCallActivityType.AllFast,
            OccurredAtUtc,
            null,
            "First line ashore",
            ActorId,
            ActorName,
            RecordedAtUtc);
}
