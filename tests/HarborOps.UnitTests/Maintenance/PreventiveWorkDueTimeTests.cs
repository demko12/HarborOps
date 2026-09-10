using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class PreventiveWorkDueTimeTests
{
    [Fact]
    public void ForDueDate_uses_the_last_second_of_the_utc_due_day()
    {
        var dueUtc = PreventiveWorkDueTime.ForDueDate(new DateOnly(2026, 9, 10));

        Assert.Equal(new DateTime(2026, 9, 10, 23, 59, 59, DateTimeKind.Utc), dueUtc);
        Assert.Equal(DateTimeKind.Utc, dueUtc.Kind);
    }

    [Theory]
    [InlineData(2026, 3, 29)]
    [InlineData(2026, 10, 25)]
    [InlineData(2026, 3, 8)]
    [InlineData(2026, 11, 1)]
    [InlineData(2026, 4, 5)]
    [InlineData(2026, 9, 27)]
    public void ForDueDate_is_not_shifted_by_daylight_saving_transitions(int year, int month, int day)
    {
        var dueDate = new DateOnly(year, month, day);

        var previous = PreventiveWorkDueTime.ForDueDate(dueDate.AddDays(-1));
        var current = PreventiveWorkDueTime.ForDueDate(dueDate);
        var next = PreventiveWorkDueTime.ForDueDate(dueDate.AddDays(1));

        Assert.Equal(TimeSpan.FromHours(24), current - previous);
        Assert.Equal(TimeSpan.FromHours(24), next - current);
        Assert.Equal(dueDate, PreventiveWorkDueTime.ToDueDate(current));
    }

    [Fact]
    public void ToDueDate_reads_the_utc_calendar_day()
    {
        var startOfDay = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        var endOfDay = new DateTime(2026, 9, 10, 23, 59, 59, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 9, 10), PreventiveWorkDueTime.ToDueDate(startOfDay));
        Assert.Equal(new DateOnly(2026, 9, 10), PreventiveWorkDueTime.ToDueDate(endOfDay));
    }

    [Fact]
    public void ToDueDate_round_trips_every_day_of_a_leap_year()
    {
        for (var dueDate = new DateOnly(2028, 1, 1); dueDate.Year == 2028; dueDate = dueDate.AddDays(1))
        {
            Assert.Equal(dueDate, PreventiveWorkDueTime.ToDueDate(PreventiveWorkDueTime.ForDueDate(dueDate)));
        }
    }
}
