using System.Globalization;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.UnitTests.Cargo;

public sealed class ReeferTemperatureReadingTests
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

    private static readonly DateTime CorrectedAtUtc =
        new(2026, 9, 19, 11, 20, 0, DateTimeKind.Utc);

    [Fact]
    public void Record_snapshots_the_profile_thresholds_and_attributes_the_actor()
    {
        var profile = Profile();

        var reading = ReeferTemperatureReading.Record(
            profile,
            1.5m,
            MeasuredAtUtc,
            "  Manual round  ",
            OperatorUserId,
            RecordedAtUtc);

        Assert.Equal(ContainerId, reading.ContainerId);
        Assert.Equal(1.5m, reading.TemperatureCelsius);
        Assert.Equal(MeasuredAtUtc, reading.MeasuredAtUtc);
        Assert.Equal(RecordedAtUtc, reading.RecordedAtUtc);
        Assert.Equal("Manual round", reading.Notes);
        Assert.Equal(OperatorUserId, reading.RecordedByUserId);
        Assert.Equal(-2m, reading.ThresholdMinimumCelsius);
        Assert.Equal(4m, reading.ThresholdMaximumCelsius);
        Assert.Equal(1m, reading.ThresholdTargetCelsius);
        Assert.Equal(profile.Version, reading.ThresholdProfileVersion);
        Assert.Equal(ReeferReadingStatus.Effective, reading.Status);
        Assert.Null(reading.CorrectsReadingId);
    }

    [Fact]
    public void Record_accepts_a_backdated_measurement()
    {
        var reading = Record(1m, MeasuredAtUtc.AddDays(-3));

        Assert.Equal(MeasuredAtUtc.AddDays(-3), reading.MeasuredAtUtc);
        Assert.Equal(RecordedAtUtc, reading.RecordedAtUtc);
    }

    [Fact]
    public void Record_accepts_a_measurement_taken_at_the_recording_instant()
    {
        var reading = Record(1m, RecordedAtUtc);

        Assert.Equal(RecordedAtUtc, reading.MeasuredAtUtc);
    }

    [Fact]
    public void Record_is_rejected_when_the_measurement_is_in_the_future()
    {
        var exception = Assert.Throws<ReeferReadingNotValidException>(
            () => Record(1m, RecordedAtUtc.AddSeconds(1)));

        Assert.Equal(ReeferReadingNotValidException.MeasuredInFutureReason, exception.Reason);
        Assert.Equal(ContainerId, exception.ContainerId);
    }

    [Theory]
    [InlineData("-60.01")]
    [InlineData("60.01")]
    public void Record_is_rejected_outside_the_supported_temperature_range(string temperature)
    {
        var exception = Assert.Throws<ReeferReadingNotValidException>(
            () => Record(Parse(temperature), MeasuredAtUtc));

        Assert.Equal(
            ReeferReadingNotValidException.OutOfSupportedRangeReason,
            exception.Reason);
    }

    [Fact]
    public void Record_is_rejected_beyond_the_supported_precision()
    {
        var exception = Assert.Throws<ReeferReadingNotValidException>(
            () => Record(Parse("1.555"), MeasuredAtUtc));

        Assert.Equal(
            ReeferReadingNotValidException.PrecisionNotSupportedReason,
            exception.Reason);
    }

    [Theory]
    [InlineData("-2")]
    [InlineData("4")]
    [InlineData("1")]
    public void A_reading_on_or_inside_a_threshold_is_within_range(string temperature)
    {
        var reading = Record(Parse(temperature), MeasuredAtUtc);

        Assert.True(reading.IsWithinThresholds);
        Assert.Null(reading.BreachDirection);
        Assert.False(reading.OpensExcursion);
    }

    [Theory]
    [InlineData("-2.01", nameof(ReeferExcursionDirection.Below))]
    [InlineData("4.01", nameof(ReeferExcursionDirection.Above))]
    public void A_reading_outside_a_threshold_breaches_in_the_matching_direction(
        string temperature,
        string expectedDirectionName)
    {
        var reading = Record(Parse(temperature), MeasuredAtUtc);

        Assert.False(reading.IsWithinThresholds);
        Assert.Equal(
            Enum.Parse<ReeferExcursionDirection>(expectedDirectionName),
            reading.BreachDirection);
        Assert.True(reading.OpensExcursion);
    }

    [Fact]
    public void A_recorded_reading_keeps_its_snapshot_when_the_profile_is_revised()
    {
        var profile = Profile();
        var reading = ReeferTemperatureReading.Record(
            profile,
            3.5m,
            MeasuredAtUtc,
            null,
            OperatorUserId,
            RecordedAtUtc);

        profile.Revise(-8m, 0m, -4m, "Tighter cold chain", ManagerUserId, CorrectedAtUtc);

        Assert.Equal(-2m, reading.ThresholdMinimumCelsius);
        Assert.Equal(4m, reading.ThresholdMaximumCelsius);
        Assert.Equal(ReeferMonitoringProfile.InitialVersion, reading.ThresholdProfileVersion);
        Assert.True(reading.IsWithinThresholds);
    }

    [Fact]
    public void A_reading_recorded_after_a_revision_uses_the_new_thresholds()
    {
        var profile = Profile();
        profile.Revise(-8m, 0m, -4m, "Tighter cold chain", ManagerUserId, ConfiguredAtUtc);

        var reading = ReeferTemperatureReading.Record(
            profile,
            3.5m,
            MeasuredAtUtc,
            null,
            OperatorUserId,
            RecordedAtUtc);

        Assert.Equal(-8m, reading.ThresholdMinimumCelsius);
        Assert.Equal(0m, reading.ThresholdMaximumCelsius);
        Assert.Equal(ReeferMonitoringProfile.InitialVersion + 1, reading.ThresholdProfileVersion);
        Assert.False(reading.IsWithinThresholds);
    }

    [Fact]
    public void Correct_supersedes_the_original_and_keeps_its_history()
    {
        var original = Record(5m, MeasuredAtUtc);

        var replacement = original.Correct(
            2m,
            MeasuredAtUtc,
            "Recalibrated probe",
            "  Probe was misread  ",
            ManagerUserId,
            CorrectedAtUtc);

        Assert.Equal(ReeferReadingStatus.Superseded, original.Status);
        Assert.Equal(replacement.Id, original.SupersededByReadingId);
        Assert.Equal("Probe was misread", original.CorrectionReason);
        Assert.Equal(ManagerUserId, original.CorrectedByUserId);
        Assert.Equal(CorrectedAtUtc, original.CorrectedAtUtc);
        Assert.Equal(5m, original.TemperatureCelsius);
        Assert.False(original.OpensExcursion);
    }

    [Fact]
    public void A_correction_is_evaluated_against_the_original_threshold_snapshot()
    {
        var profile = Profile();
        var original = ReeferTemperatureReading.Record(
            profile,
            5m,
            MeasuredAtUtc,
            null,
            OperatorUserId,
            RecordedAtUtc);

        profile.Revise(-20m, 20m, 0m, "Range widened", ManagerUserId, CorrectedAtUtc);

        var replacement = original.Correct(
            4.5m,
            MeasuredAtUtc,
            null,
            "Probe was misread",
            ManagerUserId,
            CorrectedAtUtc);

        Assert.Equal(original.Id, replacement.CorrectsReadingId);
        Assert.Equal(-2m, replacement.ThresholdMinimumCelsius);
        Assert.Equal(4m, replacement.ThresholdMaximumCelsius);
        Assert.Equal(original.ThresholdProfileVersion, replacement.ThresholdProfileVersion);
        Assert.Equal(ReeferReadingStatus.Effective, replacement.Status);
        Assert.True(replacement.OpensExcursion);
    }

    [Fact]
    public void A_correction_is_attributed_to_the_correcting_actor_and_instant()
    {
        var original = Record(5m, MeasuredAtUtc);

        var replacement = original.Correct(
            2m,
            MeasuredAtUtc,
            null,
            "Probe was misread",
            ManagerUserId,
            CorrectedAtUtc);

        Assert.Equal(ManagerUserId, replacement.RecordedByUserId);
        Assert.Equal(CorrectedAtUtc, replacement.RecordedAtUtc);
        Assert.Equal(MeasuredAtUtc, replacement.MeasuredAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Correct_requires_a_reason(string reason)
    {
        var original = Record(5m, MeasuredAtUtc);

        var exception = Assert.Throws<ReeferReadingReasonRequiredException>(
            () => original.Correct(2m, MeasuredAtUtc, null, reason, ManagerUserId, CorrectedAtUtc));

        Assert.Equal(original.Id, exception.ReadingId);
        Assert.Equal(ReeferReadingStatus.Effective, original.Status);
    }

    [Fact]
    public void Correct_is_rejected_once_the_reading_is_no_longer_effective()
    {
        var original = Record(5m, MeasuredAtUtc);
        original.Correct(2m, MeasuredAtUtc, null, "Probe was misread", ManagerUserId, CorrectedAtUtc);

        var exception = Assert.Throws<ReeferReadingNotEffectiveException>(
            () => original.Correct(
                3m,
                MeasuredAtUtc,
                null,
                "Second attempt",
                ManagerUserId,
                CorrectedAtUtc));

        Assert.Equal(ReeferReadingStatus.Superseded, exception.CurrentStatus);
        Assert.Equal(ReeferReadingNotEffectiveException.CorrectAction, exception.RequestedAction);
    }

    [Fact]
    public void Void_marks_the_reading_and_keeps_its_traceability()
    {
        var reading = Record(5m, MeasuredAtUtc);

        reading.Void("  Duplicate entry  ", ManagerUserId, CorrectedAtUtc);

        Assert.Equal(ReeferReadingStatus.Voided, reading.Status);
        Assert.Equal("Duplicate entry", reading.VoidReason);
        Assert.Equal(ManagerUserId, reading.VoidedByUserId);
        Assert.Equal(CorrectedAtUtc, reading.VoidedAtUtc);
        Assert.Equal(5m, reading.TemperatureCelsius);
        Assert.Equal(OperatorUserId, reading.RecordedByUserId);
        Assert.False(reading.OpensExcursion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Void_requires_a_reason(string reason)
    {
        var reading = Record(5m, MeasuredAtUtc);

        var exception = Assert.Throws<ReeferReadingReasonRequiredException>(
            () => reading.Void(reason, ManagerUserId, CorrectedAtUtc));

        Assert.Equal(ReeferReadingNotEffectiveException.VoidAction, exception.RequestedAction);
        Assert.Equal(ReeferReadingStatus.Effective, reading.Status);
    }

    [Fact]
    public void Void_is_rejected_once_the_reading_is_no_longer_effective()
    {
        var reading = Record(5m, MeasuredAtUtc);
        reading.Void("Duplicate entry", ManagerUserId, CorrectedAtUtc);

        var exception = Assert.Throws<ReeferReadingNotEffectiveException>(
            () => reading.Void("Again", ManagerUserId, CorrectedAtUtc));

        Assert.Equal(ReeferReadingStatus.Voided, exception.CurrentStatus);
    }

    [Fact]
    public void Correcting_a_reading_changes_its_concurrency_token()
    {
        var reading = Record(5m, MeasuredAtUtc);
        var before = reading.ConcurrencyToken;

        reading.Correct(2m, MeasuredAtUtc, null, "Probe was misread", ManagerUserId, CorrectedAtUtc);

        Assert.NotEqual(before, reading.ConcurrencyToken);
    }

    private static ReeferMonitoringProfile Profile()
    {
        return ReeferMonitoringProfile.Configure(
            ContainerId,
            isReefer: true,
            -2m,
            4m,
            1m,
            ManagerUserId,
            ConfiguredAtUtc);
    }

    private static ReeferTemperatureReading Record(decimal temperatureCelsius, DateTime measuredAtUtc)
    {
        return ReeferTemperatureReading.Record(
            Profile(),
            temperatureCelsius,
            measuredAtUtc,
            null,
            OperatorUserId,
            RecordedAtUtc);
    }

    private static decimal Parse(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}
