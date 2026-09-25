using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.UnitTests.Voyages;

public sealed class PortCallPerformanceCalculatorTests
{
    private static readonly DateTime ArrivalUtc = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AllFastUtc = ArrivalUtc.AddMinutes(90);
    private static readonly DateTime CargoStartedUtc = ArrivalUtc.AddHours(2);
    private static readonly DateTime CargoCompletedUtc = ArrivalUtc.AddHours(10);
    private static readonly DateTime DepartureUtc = ArrivalUtc.AddHours(12);

    [Fact]
    public void A_complete_statement_produces_exact_elapsed_minutes_for_every_metric()
    {
        var metrics = PortCallPerformanceCalculator.Calculate(CompleteInput());

        Assert.Equal(PortCallMetric.Measured(720), metrics.Turnaround);
        Assert.Equal(PortCallMetric.Measured(90), metrics.ArrivalToAllFast);
        Assert.Equal(PortCallMetric.Measured(480), metrics.CargoElapsed);
        Assert.Equal(PortCallMetric.Measured(120), metrics.CargoCompletedToDeparture);
    }

    [Fact]
    public void Equal_timestamps_produce_valid_zero_length_intervals()
    {
        var input = new PortCallPerformanceInput(
            ArrivalUtc,
            ArrivalUtc,
            PortCallMilestoneOccurrences.Once(ArrivalUtc),
            PortCallMilestoneOccurrences.Once(ArrivalUtc),
            PortCallMilestoneOccurrences.Once(ArrivalUtc));

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(PortCallMetric.Measured(0), metrics.Turnaround);
        Assert.Equal(PortCallMetric.Measured(0), metrics.ArrivalToAllFast);
        Assert.Equal(PortCallMetric.Measured(0), metrics.CargoElapsed);
        Assert.Equal(PortCallMetric.Measured(0), metrics.CargoCompletedToDeparture);
    }

    [Fact]
    public void A_missing_all_fast_nulls_only_the_arrival_to_all_fast_metric()
    {
        var input = CompleteInput() with { AllFast = PortCallMilestoneOccurrences.None };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(
            PortCallMetric.Unavailable(PortCallMetricQuality.Missing, PortCallActivityType.AllFast),
            metrics.ArrivalToAllFast);
        Assert.Equal(PortCallMetric.Measured(720), metrics.Turnaround);
        Assert.Equal(PortCallMetric.Measured(480), metrics.CargoElapsed);
        Assert.Equal(PortCallMetric.Measured(120), metrics.CargoCompletedToDeparture);
    }

    [Fact]
    public void A_duplicated_cargo_start_is_ambiguous_and_is_never_paired_with_a_guess()
    {
        var input = CompleteInput() with { CargoStarted = new PortCallMilestoneOccurrences(2, CargoStartedUtc) };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(
            PortCallMetric.Unavailable(PortCallMetricQuality.Ambiguous, PortCallActivityType.CargoStarted),
            metrics.CargoElapsed);
        Assert.Equal(PortCallMetric.Measured(90), metrics.ArrivalToAllFast);
        Assert.Equal(PortCallMetric.Measured(120), metrics.CargoCompletedToDeparture);
    }

    [Fact]
    public void A_duplicated_cargo_completion_affects_every_metric_that_needs_it()
    {
        var input = CompleteInput() with { CargoCompleted = new PortCallMilestoneOccurrences(3, CargoCompletedUtc) };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        var ambiguous = PortCallMetric.Unavailable(
            PortCallMetricQuality.Ambiguous,
            PortCallActivityType.CargoCompleted);
        Assert.Equal(ambiguous, metrics.CargoElapsed);
        Assert.Equal(ambiguous, metrics.CargoCompletedToDeparture);
        Assert.Equal(PortCallMetric.Measured(720), metrics.Turnaround);
        Assert.Equal(PortCallMetric.Measured(90), metrics.ArrivalToAllFast);
    }

    [Fact]
    public void The_earliest_unavailable_milestone_in_the_sequence_is_reported()
    {
        var input = CompleteInput() with
        {
            CargoStarted = new PortCallMilestoneOccurrences(2, CargoStartedUtc),
            CargoCompleted = PortCallMilestoneOccurrences.None,
        };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(
            PortCallMetric.Unavailable(PortCallMetricQuality.Ambiguous, PortCallActivityType.CargoStarted),
            metrics.CargoElapsed);
        Assert.Equal(
            PortCallMetric.Unavailable(PortCallMetricQuality.Missing, PortCallActivityType.CargoCompleted),
            metrics.CargoCompletedToDeparture);
    }

    [Fact]
    public void A_single_occurrence_without_a_timestamp_is_treated_as_missing()
    {
        var input = CompleteInput() with { AllFast = new PortCallMilestoneOccurrences(1, null) };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(
            PortCallMetric.Unavailable(PortCallMetricQuality.Missing, PortCallActivityType.AllFast),
            metrics.ArrivalToAllFast);
    }

    [Fact]
    public void Cargo_completed_before_cargo_started_is_invalid_and_not_turned_into_an_absolute_value()
    {
        var input = CompleteInput() with
        {
            CargoStarted = PortCallMilestoneOccurrences.Once(CargoCompletedUtc),
            CargoCompleted = PortCallMilestoneOccurrences.Once(CargoStartedUtc),
        };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(PortCallMetric.InvalidChronology(), metrics.CargoElapsed);
        Assert.Equal(PortCallMetric.Measured(600), metrics.CargoCompletedToDeparture);
        Assert.Equal(PortCallMetric.Measured(90), metrics.ArrivalToAllFast);
    }

    [Fact]
    public void Chronology_is_checked_per_metric_so_all_fast_after_cargo_start_stays_measurable()
    {
        var lateAllFastUtc = CargoStartedUtc.AddMinutes(30);
        var input = CompleteInput() with { AllFast = PortCallMilestoneOccurrences.Once(lateAllFastUtc) };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(PortCallMetric.Measured(150), metrics.ArrivalToAllFast);
        Assert.Equal(PortCallMetric.Measured(480), metrics.CargoElapsed);
    }

    [Fact]
    public void A_milestone_before_arrival_is_invalid()
    {
        var input = CompleteInput() with { AllFast = PortCallMilestoneOccurrences.Once(ArrivalUtc.AddMinutes(-1)) };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(PortCallMetric.InvalidChronology(), metrics.ArrivalToAllFast);
        Assert.Equal(PortCallMetric.Measured(720), metrics.Turnaround);
    }

    [Fact]
    public void A_milestone_after_departure_invalidates_every_metric_that_uses_it()
    {
        var input = CompleteInput() with
        {
            CargoCompleted = PortCallMilestoneOccurrences.Once(DepartureUtc.AddMinutes(5)),
        };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(PortCallMetric.InvalidChronology(), metrics.CargoElapsed);
        Assert.Equal(PortCallMetric.InvalidChronology(), metrics.CargoCompletedToDeparture);
        Assert.Equal(PortCallMetric.Measured(720), metrics.Turnaround);
        Assert.Equal(PortCallMetric.Measured(90), metrics.ArrivalToAllFast);
    }

    [Fact]
    public void Departure_before_arrival_invalidates_every_metric()
    {
        var input = CompleteInput() with { ActualDepartureUtc = ArrivalUtc.AddMinutes(-30) };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(PortCallMetric.InvalidChronology(), metrics.Turnaround);
        Assert.Equal(PortCallMetric.InvalidChronology(), metrics.ArrivalToAllFast);
        Assert.Equal(PortCallMetric.InvalidChronology(), metrics.CargoElapsed);
        Assert.Equal(PortCallMetric.InvalidChronology(), metrics.CargoCompletedToDeparture);
    }

    [Fact]
    public void A_missing_arrival_is_reported_on_every_metric()
    {
        var input = CompleteInput() with { ActualArrivalUtc = null };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        var missingArrival = PortCallMetric.Unavailable(
            PortCallMetricQuality.Missing,
            PortCallPerformanceCalculator.ActualArrivalMilestone);
        Assert.Equal(missingArrival, metrics.Turnaround);
        Assert.Equal(missingArrival, metrics.ArrivalToAllFast);
        Assert.Equal(missingArrival, metrics.CargoElapsed);
        Assert.Equal(missingArrival, metrics.CargoCompletedToDeparture);
    }

    [Fact]
    public void A_missing_departure_is_reported_on_every_metric()
    {
        var input = CompleteInput() with { ActualDepartureUtc = null };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        var missingDeparture = PortCallMetric.Unavailable(
            PortCallMetricQuality.Missing,
            PortCallPerformanceCalculator.ActualDepartureMilestone);
        Assert.Equal(missingDeparture, metrics.Turnaround);
        Assert.Equal(missingDeparture, metrics.ArrivalToAllFast);
        Assert.Equal(missingDeparture, metrics.CargoElapsed);
        Assert.Equal(missingDeparture, metrics.CargoCompletedToDeparture);
    }

    [Fact]
    public void Elapsed_minutes_count_completed_minutes_only()
    {
        var input = CompleteInput() with
        {
            AllFast = PortCallMilestoneOccurrences.Once(ArrivalUtc.AddMinutes(89).AddSeconds(59)),
        };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(PortCallMetric.Measured(89), metrics.ArrivalToAllFast);
    }

    [Fact]
    public void A_reversal_shorter_than_a_minute_is_still_invalid_rather_than_zero()
    {
        var input = CompleteInput() with
        {
            CargoStarted = PortCallMilestoneOccurrences.Once(CargoStartedUtc.AddSeconds(30)),
            CargoCompleted = PortCallMilestoneOccurrences.Once(CargoStartedUtc),
        };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(PortCallMetric.InvalidChronology(), metrics.CargoElapsed);
    }

    [Fact]
    public void Elapsed_time_across_a_daylight_saving_change_uses_utc_not_wall_clock()
    {
        var arrivalUtc = new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.FromHours(2)).UtcDateTime;
        var departureUtc = new DateTimeOffset(2026, 10, 25, 5, 0, 0, TimeSpan.FromHours(1)).UtcDateTime;
        var cargoCompletedUtc = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(1)).UtcDateTime;
        var input = new PortCallPerformanceInput(
            arrivalUtc,
            departureUtc,
            PortCallMilestoneOccurrences.Once(arrivalUtc),
            PortCallMilestoneOccurrences.Once(arrivalUtc),
            PortCallMilestoneOccurrences.Once(cargoCompletedUtc));

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(PortCallMetric.Measured(300), metrics.Turnaround);
        Assert.Equal(PortCallMetric.Measured(150), metrics.CargoElapsed);
        Assert.Equal(PortCallMetric.Measured(150), metrics.CargoCompletedToDeparture);
    }

    [Fact]
    public void Timestamps_with_different_offsets_are_compared_as_instants()
    {
        var arrivalUtc = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(2)).UtcDateTime;
        var allFastUtc = new DateTimeOffset(2026, 9, 1, 3, 30, 0, TimeSpan.FromHours(-5)).UtcDateTime;
        var input = CompleteInput() with
        {
            ActualArrivalUtc = arrivalUtc,
            AllFast = PortCallMilestoneOccurrences.Once(allFastUtc),
        };

        var metrics = PortCallPerformanceCalculator.Calculate(input);

        Assert.Equal(PortCallMetric.Measured(30), metrics.ArrivalToAllFast);
    }

    private static PortCallPerformanceInput CompleteInput() =>
        new(
            ArrivalUtc,
            DepartureUtc,
            PortCallMilestoneOccurrences.Once(AllFastUtc),
            PortCallMilestoneOccurrences.Once(CargoStartedUtc),
            PortCallMilestoneOccurrences.Once(CargoCompletedUtc));
}
