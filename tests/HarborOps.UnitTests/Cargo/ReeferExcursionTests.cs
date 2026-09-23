using System.Globalization;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.UnitTests.Cargo;

public sealed class ReeferExcursionTests
{
    private static readonly Guid ContainerId =
        Guid.Parse("8a1f4c2d-6b3e-4a57-9c80-1d2e3f4a5b6c");

    private static readonly Guid ManagerUserId =
        Guid.Parse("4d5e6f70-8192-4a3b-8c4d-5e6f708192a3");

    private static readonly Guid OperatorUserId =
        Guid.Parse("1a2b3c4d-5e6f-4071-8293-a4b5c6d7e8f9");

    private static readonly DateTime ConfiguredAtUtc =
        new(2026, 9, 18, 6, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime MeasuredAtUtc =
        new(2026, 9, 19, 7, 45, 0, DateTimeKind.Utc);

    private static readonly DateTime RecordedAtUtc =
        new(2026, 9, 19, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime ResolvedAtUtc =
        new(2026, 9, 19, 16, 10, 0, DateTimeKind.Utc);

    [Fact]
    public void Open_captures_the_breaching_reading_and_its_thresholds()
    {
        var reading = Record(6.25m);

        var excursion = ReeferExcursion.Open(reading);

        Assert.Equal(ContainerId, excursion.ContainerId);
        Assert.Equal(reading.Id, excursion.ReadingId);
        Assert.Equal(ReeferExcursionDirection.Above, excursion.Direction);
        Assert.Equal(6.25m, excursion.TemperatureCelsius);
        Assert.Equal(-2m, excursion.ThresholdMinimumCelsius);
        Assert.Equal(4m, excursion.ThresholdMaximumCelsius);
        Assert.Equal(MeasuredAtUtc, excursion.MeasuredAtUtc);
        Assert.Equal(RecordedAtUtc, excursion.DetectedAtUtc);
        Assert.Equal(ReeferExcursionStatus.Open, excursion.Status);
        Assert.True(excursion.IsOpen);
        Assert.Null(excursion.Disposition);
        Assert.Null(excursion.Explanation);
    }

    [Fact]
    public void Open_records_a_breach_below_the_minimum()
    {
        var excursion = ReeferExcursion.Open(Record(-9m));

        Assert.Equal(ReeferExcursionDirection.Below, excursion.Direction);
    }

    [Theory]
    [InlineData("-2")]
    [InlineData("4")]
    [InlineData("0.5")]
    public void Open_is_rejected_for_a_reading_within_its_thresholds(string temperature)
    {
        var reading = Record(Parse(temperature));

        var exception = Assert.Throws<ReeferExcursionNotBreachedException>(
            () => ReeferExcursion.Open(reading));

        Assert.Equal(reading.Id, exception.ReadingId);
    }

    [Fact]
    public void Each_breaching_reading_opens_its_own_excursion()
    {
        var first = ReeferExcursion.Open(Record(6m));
        var second = ReeferExcursion.Open(Record(-9m));

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.ReadingId, second.ReadingId);
        Assert.True(first.IsOpen);
        Assert.True(second.IsOpen);
    }

    [Theory]
    [InlineData(nameof(ReeferExcursionDisposition.CorrectiveAction))]
    [InlineData(nameof(ReeferExcursionDisposition.AcceptedDeviation))]
    public void Resolve_records_the_disposition_and_attributes_the_actor(string dispositionName)
    {
        var disposition = Enum.Parse<ReeferExcursionDisposition>(dispositionName);
        var excursion = ReeferExcursion.Open(Record(6m));

        excursion.Resolve(disposition, "  Reefer unit restarted  ", ManagerUserId, ResolvedAtUtc);

        Assert.Equal(ReeferExcursionStatus.Resolved, excursion.Status);
        Assert.False(excursion.IsOpen);
        Assert.Equal(disposition, excursion.Disposition);
        Assert.Equal("Reefer unit restarted", excursion.Explanation);
        Assert.Equal(ManagerUserId, excursion.ResolvedByUserId);
        Assert.Equal(ResolvedAtUtc, excursion.ResolvedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_requires_an_explanation(string explanation)
    {
        var excursion = ReeferExcursion.Open(Record(6m));

        var exception = Assert.Throws<ReeferExcursionExplanationRequiredException>(
            () => excursion.Resolve(
                ReeferExcursionDisposition.AcceptedDeviation,
                explanation,
                ManagerUserId,
                ResolvedAtUtc));

        Assert.Equal(excursion.Id, exception.ExcursionId);
        Assert.True(excursion.IsOpen);
    }

    [Fact]
    public void Resolve_is_rejected_once_the_excursion_is_closed()
    {
        var excursion = ReeferExcursion.Open(Record(6m));
        excursion.Resolve(
            ReeferExcursionDisposition.CorrectiveAction,
            "Reefer unit restarted",
            ManagerUserId,
            ResolvedAtUtc);

        var exception = Assert.Throws<ReeferExcursionNotOpenException>(
            () => excursion.Resolve(
                ReeferExcursionDisposition.AcceptedDeviation,
                "Changed my mind",
                ManagerUserId,
                ResolvedAtUtc));

        Assert.Equal(ReeferExcursionStatus.Resolved, exception.CurrentStatus);
        Assert.Equal(ReeferExcursionNotOpenException.ResolveAction, exception.RequestedAction);
    }

    [Fact]
    public void A_later_reading_does_not_close_an_open_excursion()
    {
        var excursion = ReeferExcursion.Open(Record(6m));

        var normal = Record(1m);

        Assert.False(normal.OpensExcursion);
        Assert.True(excursion.IsOpen);
        Assert.Equal(ReeferExcursionStatus.Open, excursion.Status);
    }

    [Fact]
    public void Supersede_links_the_replacement_and_keeps_the_original_record()
    {
        var excursion = ReeferExcursion.Open(Record(6m));
        var replacementId = Guid.Parse("c0ffee00-1111-4222-8333-444455556666");

        excursion.Supersede(replacementId, ResolvedAtUtc);

        Assert.Equal(ReeferExcursionStatus.Superseded, excursion.Status);
        Assert.Equal(replacementId, excursion.SupersededByExcursionId);
        Assert.Equal(ResolvedAtUtc, excursion.SupersededAtUtc);
        Assert.Equal(6m, excursion.TemperatureCelsius);
        Assert.Equal(ReeferExcursionDirection.Above, excursion.Direction);
        Assert.False(excursion.IsOpen);
    }

    [Fact]
    public void Supersede_without_a_replacement_still_closes_the_excursion()
    {
        var excursion = ReeferExcursion.Open(Record(6m));

        excursion.Supersede(null, ResolvedAtUtc);

        Assert.Equal(ReeferExcursionStatus.Superseded, excursion.Status);
        Assert.Null(excursion.SupersededByExcursionId);
    }

    [Fact]
    public void Supersede_is_rejected_once_the_excursion_is_resolved()
    {
        var excursion = ReeferExcursion.Open(Record(6m));
        excursion.Resolve(
            ReeferExcursionDisposition.CorrectiveAction,
            "Reefer unit restarted",
            ManagerUserId,
            ResolvedAtUtc);

        var exception = Assert.Throws<ReeferExcursionNotOpenException>(
            () => excursion.Supersede(null, ResolvedAtUtc));

        Assert.Equal(ReeferExcursionNotOpenException.SupersedeAction, exception.RequestedAction);
        Assert.Equal(ReeferExcursionStatus.Resolved, excursion.Status);
    }

    [Fact]
    public void Resolving_an_excursion_changes_its_concurrency_token()
    {
        var excursion = ReeferExcursion.Open(Record(6m));
        var before = excursion.ConcurrencyToken;

        excursion.Resolve(
            ReeferExcursionDisposition.CorrectiveAction,
            "Reefer unit restarted",
            ManagerUserId,
            ResolvedAtUtc);

        Assert.NotEqual(before, excursion.ConcurrencyToken);
    }

    [Theory]
    [InlineData(nameof(ReeferExcursionStatus.Open), true)]
    [InlineData(nameof(ReeferExcursionStatus.Resolved), false)]
    [InlineData(nameof(ReeferExcursionStatus.Superseded), false)]
    public void BlocksRelease_only_accepts_an_open_excursion(string statusName, bool expected)
    {
        var status = Enum.Parse<ReeferExcursionStatus>(statusName);

        Assert.Equal(expected, ReeferExcursion.BlocksRelease(status));
    }

    private static ReeferTemperatureReading Record(decimal temperatureCelsius)
    {
        var profile = ReeferMonitoringProfile.Configure(
            ContainerId,
            isReefer: true,
            -2m,
            4m,
            1m,
            ManagerUserId,
            ConfiguredAtUtc);

        return ReeferTemperatureReading.Record(
            profile,
            temperatureCelsius,
            MeasuredAtUtc,
            null,
            OperatorUserId,
            RecordedAtUtc);
    }

    private static decimal Parse(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}
