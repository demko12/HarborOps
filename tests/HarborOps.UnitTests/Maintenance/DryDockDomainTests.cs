using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class DryDockDomainTests
{
    private static readonly DateOnly StartDate = new(2026, 3, 1);
    private static readonly DateOnly EndDate = new(2026, 3, 20);

    private static DryDock SchedulePlanned()
    {
        return DryDock.Schedule(
            Guid.NewGuid(),
            Guid.NewGuid(),
            StartDate,
            "Hull cleaning and propeller inspection");
    }

    [Fact]
    public void A_scheduled_dry_dock_starts_planned_without_an_end_date_or_cost()
    {
        var dryDock = SchedulePlanned();

        Assert.Equal(DryDockStatus.Planned, dryDock.Status);
        Assert.Null(dryDock.EndDate);
        Assert.Null(dryDock.ActualCost);
    }

    [Theory]
    [InlineData(DryDockStatus.Planned, new[] { DryDockStatus.InProgress, DryDockStatus.Canceled })]
    [InlineData(DryDockStatus.InProgress, new[] { DryDockStatus.Completed })]
    [InlineData(DryDockStatus.Completed, new DryDockStatus[0])]
    [InlineData(DryDockStatus.Canceled, new DryDockStatus[0])]
    public void The_transition_matrix_offers_only_the_supported_moves(
        DryDockStatus status,
        DryDockStatus[] expected)
    {
        Assert.Equal(expected, DryDock.AllowedNextStatusesFor(status));
    }

    [Fact]
    public void A_planned_dry_dock_can_start()
    {
        var dryDock = SchedulePlanned();

        dryDock.Start();

        Assert.Equal(DryDockStatus.InProgress, dryDock.Status);
    }

    [Fact]
    public void A_planned_dry_dock_can_be_cancelled()
    {
        var dryDock = SchedulePlanned();

        dryDock.Cancel();

        Assert.Equal(DryDockStatus.Canceled, dryDock.Status);
    }

    [Fact]
    public void Completion_records_the_end_date_and_actual_cost()
    {
        var dryDock = SchedulePlanned();
        dryDock.Start();

        dryDock.Complete(EndDate, 125_000.50m);

        Assert.Equal(DryDockStatus.Completed, dryDock.Status);
        Assert.Equal(EndDate, dryDock.EndDate);
        Assert.Equal(125_000.50m, dryDock.ActualCost);
    }

    [Fact]
    public void A_dry_dock_that_ends_on_its_start_date_can_be_completed()
    {
        var dryDock = SchedulePlanned();
        dryDock.Start();

        dryDock.Complete(StartDate, 0m);

        Assert.Equal(StartDate, dryDock.EndDate);
        Assert.Equal(0m, dryDock.ActualCost);
    }

    [Fact]
    public void A_planned_dry_dock_cannot_be_completed()
    {
        var dryDock = SchedulePlanned();

        var exception = Assert.Throws<DryDockTransitionNotAllowedException>(
            () => dryDock.Complete(EndDate, 1_000m));

        Assert.Equal(DryDockStatus.Planned, exception.CurrentStatus);
        Assert.Equal(DryDockStatus.Completed, exception.RequestedStatus);
    }

    [Fact]
    public void A_dry_dock_in_progress_cannot_be_cancelled()
    {
        var dryDock = SchedulePlanned();
        dryDock.Start();

        var exception = Assert.Throws<DryDockTransitionNotAllowedException>(dryDock.Cancel);

        Assert.Equal(DryDockStatus.InProgress, exception.CurrentStatus);
        Assert.Equal(DryDockStatus.Canceled, exception.RequestedStatus);
    }

    [Fact]
    public void A_dry_dock_cannot_be_started_twice()
    {
        var dryDock = SchedulePlanned();
        dryDock.Start();

        var exception = Assert.Throws<DryDockTransitionNotAllowedException>(dryDock.Start);

        Assert.Equal(DryDockStatus.InProgress, exception.CurrentStatus);
    }

    [Fact]
    public void A_completed_dry_dock_accepts_no_further_transition()
    {
        var dryDock = SchedulePlanned();
        dryDock.Start();
        dryDock.Complete(EndDate, 1_000m);

        Assert.Throws<DryDockTransitionNotAllowedException>(dryDock.Start);
        Assert.Throws<DryDockTransitionNotAllowedException>(dryDock.Cancel);
        Assert.Throws<DryDockTransitionNotAllowedException>(() => dryDock.Complete(EndDate, 1_000m));
    }

    [Fact]
    public void A_cancelled_dry_dock_accepts_no_further_transition()
    {
        var dryDock = SchedulePlanned();
        dryDock.Cancel();

        Assert.Throws<DryDockTransitionNotAllowedException>(dryDock.Start);
        Assert.Throws<DryDockTransitionNotAllowedException>(() => dryDock.Complete(EndDate, 1_000m));
    }

    [Fact]
    public void An_end_date_before_the_start_date_is_rejected()
    {
        var dryDock = SchedulePlanned();
        dryDock.Start();

        Assert.Throws<DryDockCompletionInvalidException>(
            () => dryDock.Complete(StartDate.AddDays(-1), 1_000m));
    }

    [Fact]
    public void A_negative_actual_cost_is_rejected()
    {
        var dryDock = SchedulePlanned();
        dryDock.Start();

        Assert.Throws<DryDockCompletionInvalidException>(() => dryDock.Complete(EndDate, -0.01m));
    }

    [Fact]
    public void A_rejected_completion_leaves_the_status_dates_and_cost_untouched()
    {
        var dryDock = SchedulePlanned();
        dryDock.Start();

        Assert.Throws<DryDockCompletionInvalidException>(
            () => dryDock.Complete(StartDate.AddDays(-1), 1_000m));

        Assert.Equal(DryDockStatus.InProgress, dryDock.Status);
        Assert.Null(dryDock.EndDate);
        Assert.Null(dryDock.ActualCost);
    }

    [Fact]
    public void A_rejected_transition_leaves_the_status_dates_and_cost_untouched()
    {
        var dryDock = SchedulePlanned();

        Assert.Throws<DryDockTransitionNotAllowedException>(
            () => dryDock.Complete(EndDate, 1_000m));

        Assert.Equal(DryDockStatus.Planned, dryDock.Status);
        Assert.Null(dryDock.EndDate);
        Assert.Null(dryDock.ActualCost);
    }
}
