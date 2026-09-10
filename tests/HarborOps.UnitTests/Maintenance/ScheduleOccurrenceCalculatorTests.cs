using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class ScheduleOccurrenceCalculatorTests
{
    private static readonly DateOnly MonthEnd = new(2026, 1, 31);

    [Theory]
    [InlineData("Daily", 2026, 2, 1)]
    [InlineData("Weekly", 2026, 2, 7)]
    [InlineData("Monthly", 2026, 2, 28)]
    [InlineData("Quarterly", 2026, 4, 30)]
    [InlineData("SemiAnnual", 2026, 7, 31)]
    [InlineData("Annual", 2027, 1, 31)]
    public void Advance_moves_one_step_for_every_supported_frequency(
        string frequency,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var next = ScheduleOccurrenceCalculator.Advance(MonthEnd, Parse(frequency), 1);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), next);
    }

    [Fact]
    public void Advance_with_zero_steps_returns_the_anchor()
    {
        Assert.Equal(MonthEnd, ScheduleOccurrenceCalculator.Advance(MonthEnd, MaintenanceFrequency.Monthly, 0));
    }

    [Fact]
    public void Advance_leaves_a_running_hours_anchor_untouched()
    {
        Assert.Equal(MonthEnd, ScheduleOccurrenceCalculator.Advance(MonthEnd, MaintenanceFrequency.RunningHours, 3));
    }

    [Theory]
    [InlineData("Daily", true)]
    [InlineData("Weekly", true)]
    [InlineData("Monthly", true)]
    [InlineData("Quarterly", true)]
    [InlineData("SemiAnnual", true)]
    [InlineData("Annual", true)]
    [InlineData("RunningHours", false)]
    public void IsTimeBased_supports_only_calendar_frequencies(string frequency, bool expected)
    {
        Assert.Equal(expected, ScheduleOccurrenceCalculator.IsTimeBased(Parse(frequency)));
    }

    [Fact]
    public void Monthly_series_keeps_the_month_end_anchor_across_short_months()
    {
        var dueDates = ScheduleOccurrenceCalculator.DueDatesInWindow(
            MaintenanceFrequency.Monthly,
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 5, 31));

        Assert.Equal(
            [
                new DateOnly(2026, 1, 31),
                new DateOnly(2026, 2, 28),
                new DateOnly(2026, 3, 31),
                new DateOnly(2026, 4, 30),
                new DateOnly(2026, 5, 31),
            ],
            dueDates);
    }

    [Fact]
    public void Quarterly_series_crosses_the_year_boundary_from_a_month_end()
    {
        var dueDates = ScheduleOccurrenceCalculator.DueDatesInWindow(
            MaintenanceFrequency.Quarterly,
            new DateOnly(2026, 11, 30),
            new DateOnly(2026, 11, 1),
            new DateOnly(2027, 8, 31));

        Assert.Equal(
            [
                new DateOnly(2026, 11, 30),
                new DateOnly(2027, 2, 28),
                new DateOnly(2027, 5, 30),
                new DateOnly(2027, 8, 30),
            ],
            dueDates);
    }

    [Fact]
    public void SemiAnnual_series_crosses_the_year_boundary()
    {
        var dueDates = ScheduleOccurrenceCalculator.DueDatesInWindow(
            MaintenanceFrequency.SemiAnnual,
            new DateOnly(2026, 8, 31),
            new DateOnly(2026, 8, 1),
            new DateOnly(2027, 8, 31));

        Assert.Equal(
            [
                new DateOnly(2026, 8, 31),
                new DateOnly(2027, 2, 28),
                new DateOnly(2027, 8, 31),
            ],
            dueDates);
    }

    [Fact]
    public void Daily_and_weekly_series_roll_over_the_year_end()
    {
        var daily = ScheduleOccurrenceCalculator.DueDatesInWindow(
            MaintenanceFrequency.Daily,
            new DateOnly(2026, 12, 31),
            new DateOnly(2026, 12, 31),
            new DateOnly(2027, 1, 1));

        var weekly = ScheduleOccurrenceCalculator.DueDatesInWindow(
            MaintenanceFrequency.Weekly,
            new DateOnly(2026, 12, 28),
            new DateOnly(2026, 12, 28),
            new DateOnly(2027, 1, 4));

        Assert.Equal([new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1)], daily);
        Assert.Equal([new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 4)], weekly);
    }

    [Fact]
    public void Annual_series_from_a_leap_day_returns_to_it_only_in_the_next_leap_year()
    {
        var dueDates = ScheduleOccurrenceCalculator.DueDatesInWindow(
            MaintenanceFrequency.Annual,
            new DateOnly(2028, 2, 29),
            new DateOnly(2028, 1, 1),
            new DateOnly(2032, 3, 1));

        Assert.Equal(
            [
                new DateOnly(2028, 2, 29),
                new DateOnly(2029, 2, 28),
                new DateOnly(2030, 2, 28),
                new DateOnly(2031, 2, 28),
                new DateOnly(2032, 2, 29),
            ],
            dueDates);
    }

    [Fact]
    public void DueDatesInWindow_keeps_an_overdue_due_date_and_skips_missed_recurrences()
    {
        var dueDates = ScheduleOccurrenceCalculator.DueDatesInWindow(
            MaintenanceFrequency.Weekly,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31));

        Assert.Equal(
            [
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 3, 5),
                new DateOnly(2026, 3, 12),
                new DateOnly(2026, 3, 19),
                new DateOnly(2026, 3, 26),
            ],
            dueDates);
    }

    [Fact]
    public void DueDatesInWindow_includes_the_window_end_and_excludes_later_dates()
    {
        var inclusive = ScheduleOccurrenceCalculator.DueDatesInWindow(
            MaintenanceFrequency.Monthly,
            new DateOnly(2026, 9, 15),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 10, 15));

        var exclusive = ScheduleOccurrenceCalculator.DueDatesInWindow(
            MaintenanceFrequency.Monthly,
            new DateOnly(2026, 9, 15),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 10, 14));

        Assert.Equal([new DateOnly(2026, 9, 15), new DateOnly(2026, 10, 15)], inclusive);
        Assert.Equal([new DateOnly(2026, 9, 15)], exclusive);
    }

    [Fact]
    public void DueDatesInWindow_returns_nothing_when_the_next_due_date_is_after_the_window()
    {
        var dueDates = ScheduleOccurrenceCalculator.DueDatesInWindow(
            MaintenanceFrequency.Monthly,
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 10, 31));

        Assert.Empty(dueDates);
    }

    [Fact]
    public void DueDatesInWindow_returns_nothing_for_a_running_hours_schedule()
    {
        var dueDates = ScheduleOccurrenceCalculator.DueDatesInWindow(
            MaintenanceFrequency.RunningHours,
            new DateOnly(2026, 9, 15),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 10, 31));

        Assert.Empty(dueDates);
    }

    [Fact]
    public void DueDatesInWindow_is_bounded_per_schedule()
    {
        var anchor = new DateOnly(2026, 1, 1);

        var dueDates = ScheduleOccurrenceCalculator.DueDatesInWindow(
            MaintenanceFrequency.Daily,
            anchor,
            anchor,
            new DateOnly(2026, 12, 31));

        Assert.Equal(ScheduleOccurrenceCalculator.MaxOccurrencesPerSchedule, dueDates.Count);
        Assert.Equal(anchor.AddDays(ScheduleOccurrenceCalculator.MaxOccurrencesPerSchedule - 1), dueDates[^1]);
    }

    private static MaintenanceFrequency Parse(string frequency)
    {
        return Enum.Parse<MaintenanceFrequency>(frequency);
    }
}
