using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class MaintenanceScheduleDomainTests
{
    private static readonly DateOnly CompletedOn = new(2026, 1, 31);

    [Fact]
    public void Create_stores_the_frequency_name()
    {
        var schedule = MaintenanceSchedule.Create(
            "Main engine overhaul",
            MaintenanceFrequency.Quarterly,
            new DateOnly(2026, 3, 1),
            null);

        Assert.Equal("Quarterly", schedule.Frequency);
        Assert.True(schedule.TryGetFrequency(out var parsed));
        Assert.Equal(MaintenanceFrequency.Quarterly, parsed);
    }

    [Theory]
    [InlineData("Daily", 2026, 2, 1)]
    [InlineData("Weekly", 2026, 2, 7)]
    [InlineData("Monthly", 2026, 2, 28)]
    [InlineData("Quarterly", 2026, 4, 30)]
    [InlineData("SemiAnnual", 2026, 7, 31)]
    [InlineData("Annual", 2027, 1, 31)]
    public void RecordCompletion_advances_the_next_due_date_for_calendar_frequencies(
        string frequency,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var schedule = MaintenanceSchedule.Create(
            "Inspection",
            Enum.Parse<MaintenanceFrequency>(frequency),
            new DateOnly(2026, 1, 1),
            null);

        schedule.RecordCompletion(Enum.Parse<MaintenanceFrequency>(frequency), CompletedOn, null);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), schedule.NextDueDate);
    }

    [Fact]
    public void RecordCompletion_leaves_the_date_untouched_for_a_running_hours_schedule()
    {
        var schedule = MaintenanceSchedule.Create(
            "Bearing check",
            MaintenanceFrequency.RunningHours,
            null,
            500);

        schedule.RecordCompletion(MaintenanceFrequency.RunningHours, CompletedOn, 1000);

        Assert.Null(schedule.NextDueDate);
        Assert.Equal(1000, schedule.NextDueHours);
    }

    [Fact]
    public void RecordCompletion_keeps_the_current_hours_when_none_are_supplied()
    {
        var schedule = MaintenanceSchedule.Create(
            "Filter change",
            MaintenanceFrequency.Monthly,
            new DateOnly(2026, 1, 1),
            750);
        schedule.RecordCompletion(MaintenanceFrequency.Monthly, CompletedOn, null);
        schedule.RecordCompletion(schedule.TryGetFrequency(out var frequency) ? frequency : default, CompletedOn, null);

        Assert.Equal(new DateOnly(2026, 2, 28), schedule.NextDueDate);
        Assert.Equal(750, schedule.NextDueHours);
    }

    [Fact]
    public void RecordCompletion_replaces_the_hours_when_they_are_supplied()
    {
        var schedule = MaintenanceSchedule.Create(
            "Filter change",
            MaintenanceFrequency.Monthly,
            new DateOnly(2026, 1, 1),
            750);

        schedule.RecordCompletion(MaintenanceFrequency.Monthly, CompletedOn, 1500);

        Assert.Equal(1500, schedule.NextDueHours);
    }

    [Fact]
    public void TryGetFrequency_rejects_a_stored_value_outside_the_supported_set()
    {
        var schedule = MaintenanceSchedule.Create(
            "Main engine inspection",
            MaintenanceFrequency.SemiAnnual,
            new DateOnly(2026, 8, 23),
            12000);

        typeof(MaintenanceSchedule)
            .GetProperty(nameof(MaintenanceSchedule.Frequency))!
            .SetValue(schedule, "Every 6 months");

        Assert.False(schedule.TryGetFrequency(out _));
    }

    [Theory]
    [InlineData(2026, 8, 17, true)]
    [InlineData(2026, 8, 18, false)]
    [InlineData(2026, 8, 19, false)]
    public void IsOverdue_compares_the_next_due_date_with_the_captured_day(
        int year,
        int month,
        int day,
        bool expected)
    {
        var today = new DateOnly(2026, 8, 18);

        Assert.Equal(expected, MaintenanceSchedule.IsOverdue(new DateOnly(year, month, day), today));
    }

    [Fact]
    public void IsOverdue_is_false_without_a_next_due_date()
    {
        Assert.False(MaintenanceSchedule.IsOverdue(null, new DateOnly(2026, 8, 18)));
    }

    [Theory]
    [InlineData(2026, 8, 18, 0)]
    [InlineData(2026, 8, 21, 3)]
    [InlineData(2026, 8, 15, -3)]
    public void DaysUntilDue_counts_whole_days_from_the_captured_day(
        int year,
        int month,
        int day,
        int expected)
    {
        var today = new DateOnly(2026, 8, 18);

        Assert.Equal(expected, MaintenanceSchedule.DaysUntilDue(new DateOnly(year, month, day), today));
    }

    [Fact]
    public void DaysUntilDue_is_null_without_a_next_due_date()
    {
        Assert.Null(MaintenanceSchedule.DaysUntilDue(null, new DateOnly(2026, 8, 18)));
    }

    [Theory]
    [InlineData("Daily", 2026, 9, 16)]
    [InlineData("Weekly", 2026, 9, 22)]
    [InlineData("Monthly", 2026, 10, 15)]
    [InlineData("Quarterly", 2026, 12, 15)]
    [InlineData("SemiAnnual", 2027, 3, 15)]
    [InlineData("Annual", 2027, 9, 15)]
    public void AdvanceAfterOccurrence_moves_the_next_due_date_one_step_past_the_occurrence(
    string frequency,
    int expectedYear,
    int expectedMonth,
    int expectedDay)
    {
        var parsed = Enum.Parse<MaintenanceFrequency>(frequency);
        var occurrenceDueDate = new DateOnly(2026, 9, 15);
        var schedule = MaintenanceSchedule.Create("Inspection", parsed, occurrenceDueDate, null);

        schedule.AdvanceAfterOccurrence(parsed, occurrenceDueDate);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), schedule.NextDueDate);
    }

    [Fact]
    public void AdvanceAfterOccurrence_is_applied_only_once_for_the_same_occurrence()
    {
        var occurrenceDueDate = new DateOnly(2026, 9, 15);
        var schedule = MaintenanceSchedule.Create("Inspection", MaintenanceFrequency.Monthly, occurrenceDueDate, null);

        schedule.AdvanceAfterOccurrence(MaintenanceFrequency.Monthly, occurrenceDueDate);
        schedule.AdvanceAfterOccurrence(MaintenanceFrequency.Monthly, occurrenceDueDate);

        Assert.Equal(new DateOnly(2026, 10, 15), schedule.NextDueDate);
    }

    [Fact]
    public void AdvanceAfterOccurrence_never_moves_the_schedule_backwards()
    {
        var schedule = MaintenanceSchedule.Create(
            "Inspection",
            MaintenanceFrequency.Monthly,
            new DateOnly(2026, 12, 1),
            null);

        schedule.AdvanceAfterOccurrence(MaintenanceFrequency.Monthly, new DateOnly(2026, 9, 15));

        Assert.Equal(new DateOnly(2026, 12, 1), schedule.NextDueDate);
    }

    [Fact]
    public void AdvanceAfterOccurrence_clamps_a_month_end_occurrence()
    {
        var occurrenceDueDate = new DateOnly(2026, 1, 31);
        var schedule = MaintenanceSchedule.Create("Inspection", MaintenanceFrequency.Monthly, occurrenceDueDate, null);

        schedule.AdvanceAfterOccurrence(MaintenanceFrequency.Monthly, occurrenceDueDate);

        Assert.Equal(new DateOnly(2026, 2, 28), schedule.NextDueDate);
    }

    [Fact]
    public void TryGetTimeBasedFrequency_accepts_a_dated_calendar_schedule()
    {
        var schedule = MaintenanceSchedule.Create(
            "Inspection",
            MaintenanceFrequency.Quarterly,
            new DateOnly(2026, 9, 15),
            null);

        Assert.True(schedule.TryGetTimeBasedFrequency(out var frequency));
        Assert.Equal(MaintenanceFrequency.Quarterly, frequency);
    }

    [Fact]
    public void TryGetTimeBasedFrequency_rejects_an_hour_only_schedule()
    {
        var schedule = MaintenanceSchedule.Create("Bearing check", MaintenanceFrequency.RunningHours, null, 500);

        Assert.False(schedule.TryGetTimeBasedFrequency(out _));
    }

    [Fact]
    public void TryGetTimeBasedFrequency_rejects_a_calendar_schedule_without_a_next_due_date()
    {
        var schedule = MaintenanceSchedule.Create("Inspection", MaintenanceFrequency.Monthly, null, null);

        Assert.False(schedule.TryGetTimeBasedFrequency(out _));
    }
}
