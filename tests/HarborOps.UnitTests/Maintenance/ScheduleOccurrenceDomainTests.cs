using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class ScheduleOccurrenceDomainTests
{
    private static readonly DateOnly DueDate = new(2026, 9, 15);
    private static readonly DateTime GeneratedUtc = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ResolvedUtc = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Generate_links_the_work_order_in_the_generated_state()
    {
        var scheduleId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();

        var occurrence = ScheduleOccurrence.Generate(scheduleId, DueDate, workOrderId, GeneratedUtc);

        Assert.NotEqual(Guid.Empty, occurrence.Id);
        Assert.Equal(scheduleId, occurrence.ScheduleId);
        Assert.Equal(DueDate, occurrence.DueDate);
        Assert.Equal(workOrderId, occurrence.WorkOrderId);
        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.Equal(GeneratedUtc, occurrence.GeneratedUtc);
        Assert.Null(occurrence.ResolvedUtc);
        Assert.Null(occurrence.ReplacedWorkOrderId);
        Assert.True(occurrence.IsLinkedTo(workOrderId));
    }

    [Fact]
    public void MarkCompleted_resolves_the_occurrence_for_its_linked_work_order()
    {
        var workOrderId = Guid.NewGuid();
        var occurrence = ScheduleOccurrence.Generate(Guid.NewGuid(), DueDate, workOrderId, GeneratedUtc);

        occurrence.MarkCompleted(workOrderId, ResolvedUtc);

        Assert.Equal(ScheduleOccurrenceState.Completed, occurrence.State);
        Assert.Equal(ResolvedUtc, occurrence.ResolvedUtc);
    }

    [Fact]
    public void MarkCancelled_resolves_the_occurrence_for_its_linked_work_order()
    {
        var workOrderId = Guid.NewGuid();
        var occurrence = ScheduleOccurrence.Generate(Guid.NewGuid(), DueDate, workOrderId, GeneratedUtc);

        occurrence.MarkCancelled(workOrderId, ResolvedUtc);

        Assert.Equal(ScheduleOccurrenceState.Cancelled, occurrence.State);
        Assert.Equal(ResolvedUtc, occurrence.ResolvedUtc);
    }

    [Fact]
    public void A_completed_occurrence_cannot_be_completed_again()
    {
        var workOrderId = Guid.NewGuid();
        var occurrence = CreateIn(ScheduleOccurrenceState.Completed, workOrderId);

        var exception = Assert.Throws<ScheduleOccurrenceTransitionNotAllowedException>(
            () => occurrence.MarkCompleted(workOrderId, ResolvedUtc));

        Assert.Equal(occurrence.Id, exception.OccurrenceId);
        Assert.Equal(ScheduleOccurrenceState.Completed, exception.CurrentState);
        Assert.Equal(ScheduleOccurrenceState.Completed, exception.RequestedState);
    }

    [Fact]
    public void A_completed_occurrence_cannot_be_cancelled()
    {
        var workOrderId = Guid.NewGuid();
        var occurrence = CreateIn(ScheduleOccurrenceState.Completed, workOrderId);

        var exception = Assert.Throws<ScheduleOccurrenceTransitionNotAllowedException>(
            () => occurrence.MarkCancelled(workOrderId, ResolvedUtc));

        Assert.Equal(ScheduleOccurrenceState.Completed, exception.CurrentState);
        Assert.Equal(ScheduleOccurrenceState.Completed, occurrence.State);
    }

    [Fact]
    public void A_cancelled_occurrence_cannot_be_completed()
    {
        var workOrderId = Guid.NewGuid();
        var occurrence = CreateIn(ScheduleOccurrenceState.Cancelled, workOrderId);

        var exception = Assert.Throws<ScheduleOccurrenceTransitionNotAllowedException>(
            () => occurrence.MarkCompleted(workOrderId, ResolvedUtc));

        Assert.Equal(ScheduleOccurrenceState.Cancelled, exception.CurrentState);
        Assert.Equal(ScheduleOccurrenceState.Completed, exception.RequestedState);
    }

    [Fact]
    public void MarkCompleted_rejects_a_work_order_that_is_not_linked()
    {
        var linkedWorkOrderId = Guid.NewGuid();
        var otherWorkOrderId = Guid.NewGuid();
        var occurrence = ScheduleOccurrence.Generate(Guid.NewGuid(), DueDate, linkedWorkOrderId, GeneratedUtc);

        var exception = Assert.Throws<ScheduleOccurrenceLinkInvalidException>(
            () => occurrence.MarkCompleted(otherWorkOrderId, ResolvedUtc));

        Assert.Equal(linkedWorkOrderId, exception.LinkedWorkOrderId);
        Assert.Equal(otherWorkOrderId, exception.RequestedWorkOrderId);
        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
    }

    [Fact]
    public void MarkCancelled_rejects_a_work_order_that_is_not_linked()
    {
        var occurrence = ScheduleOccurrence.Generate(Guid.NewGuid(), DueDate, Guid.NewGuid(), GeneratedUtc);

        Assert.Throws<ScheduleOccurrenceLinkInvalidException>(
            () => occurrence.MarkCancelled(Guid.NewGuid(), ResolvedUtc));

        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.Null(occurrence.ResolvedUtc);
    }

    [Fact]
    public void Replace_links_the_new_work_order_and_keeps_the_cancelled_one()
    {
        var cancelledWorkOrderId = Guid.NewGuid();
        var replacementWorkOrderId = Guid.NewGuid();
        var replacedUtc = ResolvedUtc.AddHours(1);
        var occurrence = CreateIn(ScheduleOccurrenceState.Cancelled, cancelledWorkOrderId);

        occurrence.Replace(replacementWorkOrderId, replacedUtc);

        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.Equal(replacementWorkOrderId, occurrence.WorkOrderId);
        Assert.Equal(cancelledWorkOrderId, occurrence.ReplacedWorkOrderId);
        Assert.Equal(replacedUtc, occurrence.GeneratedUtc);
        Assert.Null(occurrence.ResolvedUtc);
        Assert.Equal(DueDate, occurrence.DueDate);
    }

    [Theory]
    [InlineData(ScheduleOccurrenceState.Generated)]
    [InlineData(ScheduleOccurrenceState.Completed)]
    public void Replace_rejects_an_occurrence_that_is_not_cancelled(ScheduleOccurrenceState state)
    {
        var workOrderId = Guid.NewGuid();
        var occurrence = CreateIn(state, workOrderId);

        var exception = Assert.Throws<ScheduleOccurrenceTransitionNotAllowedException>(
            () => occurrence.Replace(Guid.NewGuid(), ResolvedUtc));

        Assert.Equal(state, exception.CurrentState);
        Assert.Equal(ScheduleOccurrenceState.Generated, exception.RequestedState);
        Assert.Equal(workOrderId, occurrence.WorkOrderId);
        Assert.Null(occurrence.ReplacedWorkOrderId);
    }

    [Fact]
    public void After_a_replacement_only_the_new_work_order_can_complete_the_occurrence()
    {
        var cancelledWorkOrderId = Guid.NewGuid();
        var replacementWorkOrderId = Guid.NewGuid();
        var occurrence = CreateIn(ScheduleOccurrenceState.Cancelled, cancelledWorkOrderId);
        occurrence.Replace(replacementWorkOrderId, ResolvedUtc);

        Assert.Throws<ScheduleOccurrenceLinkInvalidException>(
            () => occurrence.MarkCompleted(cancelledWorkOrderId, ResolvedUtc));

        occurrence.MarkCompleted(replacementWorkOrderId, ResolvedUtc);

        Assert.Equal(ScheduleOccurrenceState.Completed, occurrence.State);
    }

    [Theory]
    [InlineData(ScheduleOccurrenceState.Pending, -1, true)]
    [InlineData(ScheduleOccurrenceState.Generated, -1, true)]
    [InlineData(ScheduleOccurrenceState.Completed, -1, false)]
    [InlineData(ScheduleOccurrenceState.Cancelled, -1, false)]
    [InlineData(ScheduleOccurrenceState.Pending, 0, false)]
    [InlineData(ScheduleOccurrenceState.Generated, 1, false)]
    public void IsOverdue_applies_only_to_open_occurrences_before_the_captured_day(
        ScheduleOccurrenceState state,
        int daysFromToday,
        bool expected)
    {
        var today = new DateOnly(2026, 9, 10);

        Assert.Equal(expected, ScheduleOccurrence.IsOverdue(state, today.AddDays(daysFromToday), today));
    }

    private static ScheduleOccurrence CreateIn(ScheduleOccurrenceState state, Guid workOrderId)
    {
        var occurrence = ScheduleOccurrence.Generate(Guid.NewGuid(), DueDate, workOrderId, GeneratedUtc);

        switch (state)
        {
            case ScheduleOccurrenceState.Completed:
                occurrence.MarkCompleted(workOrderId, ResolvedUtc);
                break;

            case ScheduleOccurrenceState.Cancelled:
                occurrence.MarkCancelled(workOrderId, ResolvedUtc);
                break;

            default:
                break;
        }

        return occurrence;
    }
}
