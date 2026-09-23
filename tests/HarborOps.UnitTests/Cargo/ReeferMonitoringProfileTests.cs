using System.Globalization;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.UnitTests.Cargo;

public sealed class ReeferMonitoringProfileTests
{
    private static readonly Guid ContainerId =
        Guid.Parse("8a1f4c2d-6b3e-4a57-9c80-1d2e3f4a5b6c");

    private static readonly Guid ManagerUserId =
        Guid.Parse("4d5e6f70-8192-4a3b-8c4d-5e6f708192a3");

    private static readonly Guid SupervisorUserId =
        Guid.Parse("9b8a7c6d-5e4f-4032-9182-736455647382");

    private static readonly DateTime ConfiguredAtUtc =
        new(2026, 9, 18, 6, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime RevisedAtUtc =
        new(2026, 9, 19, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Configure_starts_at_the_initial_version_and_attributes_the_actor()
    {
        var profile = Configure();

        Assert.Equal(ContainerId, profile.ContainerId);
        Assert.Equal(-2m, profile.MinimumCelsius);
        Assert.Equal(4m, profile.MaximumCelsius);
        Assert.Equal(1m, profile.TargetCelsius);
        Assert.Equal(ReeferMonitoringProfile.InitialVersion, profile.Version);
        Assert.Equal(ManagerUserId, profile.CreatedByUserId);
        Assert.Equal(ConfiguredAtUtc, profile.CreatedAtUtc);
        Assert.Equal(ManagerUserId, profile.UpdatedByUserId);
        Assert.Equal(ConfiguredAtUtc, profile.UpdatedAtUtc);
    }

    [Fact]
    public void Configure_is_rejected_for_a_container_that_is_not_a_reefer()
    {
        var exception = Assert.Throws<ReeferMonitoringNotAllowedException>(
            () => ReeferMonitoringProfile.Configure(
                ContainerId,
                isReefer: false,
                -2m,
                4m,
                1m,
                ManagerUserId,
                ConfiguredAtUtc));

        Assert.Equal(ContainerId, exception.ContainerId);
    }

    [Fact]
    public void Configure_is_rejected_when_the_minimum_is_above_the_maximum()
    {
        var exception = Assert.Throws<ReeferThresholdsNotValidException>(
            () => Configure(4m, -2m, 1m));

        Assert.Equal(
            ReeferThresholdsNotValidException.MinimumAboveMaximumReason,
            exception.Reason);
    }

    [Theory]
    [InlineData("-2.01")]
    [InlineData("4.01")]
    public void Configure_is_rejected_when_the_target_is_outside_the_range(string target)
    {
        var exception = Assert.Throws<ReeferThresholdsNotValidException>(
            () => Configure(-2m, 4m, Parse(target)));

        Assert.Equal(ReeferThresholdsNotValidException.TargetOutOfRangeReason, exception.Reason);
    }

    [Theory]
    [InlineData("-60.01", "60")]
    [InlineData("-60", "60.01")]
    public void Configure_is_rejected_outside_the_supported_temperature_range(
        string minimum,
        string maximum)
    {
        var exception = Assert.Throws<ReeferThresholdsNotValidException>(
            () => Configure(Parse(minimum), Parse(maximum), 0m));

        Assert.Equal(
            ReeferThresholdsNotValidException.OutOfSupportedRangeReason,
            exception.Reason);
    }

    [Fact]
    public void Configure_accepts_the_supported_range_boundaries()
    {
        var profile = Configure(-60m, 60m, 60m);

        Assert.Equal(-60m, profile.MinimumCelsius);
        Assert.Equal(60m, profile.MaximumCelsius);
        Assert.Equal(60m, profile.TargetCelsius);
    }

    [Theory]
    [InlineData("-2.001", "4", "1")]
    [InlineData("-2", "4.005", "1")]
    [InlineData("-2", "4", "1.125")]
    public void Configure_is_rejected_beyond_the_supported_precision(
        string minimum,
        string maximum,
        string target)
    {
        var exception = Assert.Throws<ReeferThresholdsNotValidException>(
            () => Configure(Parse(minimum), Parse(maximum), Parse(target)));

        Assert.Equal(
            ReeferThresholdsNotValidException.PrecisionNotSupportedReason,
            exception.Reason);
    }

    [Fact]
    public void RecordConfiguration_captures_the_initial_thresholds_without_previous_values()
    {
        var profile = Configure();

        var change = profile.RecordConfiguration("  Initial cold chain setup  ");

        Assert.Equal(profile.Id, change.ReeferMonitoringProfileId);
        Assert.Equal(ReeferMonitoringProfile.InitialVersion, change.Version);
        Assert.Null(change.FromMinimumCelsius);
        Assert.Null(change.FromMaximumCelsius);
        Assert.Null(change.FromTargetCelsius);
        Assert.Equal(-2m, change.ToMinimumCelsius);
        Assert.Equal(4m, change.ToMaximumCelsius);
        Assert.Equal(1m, change.ToTargetCelsius);
        Assert.Equal("Initial cold chain setup", change.Reason);
        Assert.Equal(ManagerUserId, change.ActorUserId);
        Assert.Equal(ConfiguredAtUtc, change.OccurredUtc);
    }

    [Fact]
    public void Revise_bumps_the_version_and_records_both_sides_of_the_change()
    {
        var profile = Configure();

        var change = profile.Revise(
            -5m,
            2m,
            -1m,
            "  Shipper tightened the cold chain  ",
            SupervisorUserId,
            RevisedAtUtc);

        Assert.Equal(-5m, profile.MinimumCelsius);
        Assert.Equal(2m, profile.MaximumCelsius);
        Assert.Equal(-1m, profile.TargetCelsius);
        Assert.Equal(ReeferMonitoringProfile.InitialVersion + 1, profile.Version);
        Assert.Equal(SupervisorUserId, profile.UpdatedByUserId);
        Assert.Equal(RevisedAtUtc, profile.UpdatedAtUtc);

        Assert.Equal(profile.Version, change.Version);
        Assert.Equal(-2m, change.FromMinimumCelsius);
        Assert.Equal(4m, change.FromMaximumCelsius);
        Assert.Equal(1m, change.FromTargetCelsius);
        Assert.Equal(-5m, change.ToMinimumCelsius);
        Assert.Equal(2m, change.ToMaximumCelsius);
        Assert.Equal(-1m, change.ToTargetCelsius);
        Assert.Equal("Shipper tightened the cold chain", change.Reason);
        Assert.Equal(SupervisorUserId, change.ActorUserId);
        Assert.Equal(RevisedAtUtc, change.OccurredUtc);
    }

    [Fact]
    public void Revise_changes_the_concurrency_token()
    {
        var profile = Configure();
        var before = profile.ConcurrencyToken;

        profile.Revise(-5m, 2m, -1m, "Tighter range", SupervisorUserId, RevisedAtUtc);

        Assert.NotEqual(before, profile.ConcurrencyToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Revise_requires_a_reason(string reason)
    {
        var profile = Configure();

        var exception = Assert.Throws<ReeferProfileChangeReasonRequiredException>(
            () => profile.Revise(-5m, 2m, -1m, reason, SupervisorUserId, RevisedAtUtc));

        Assert.Equal(profile.Id, exception.ProfileId);
    }

    [Fact]
    public void Revise_leaves_the_profile_untouched_when_the_thresholds_are_invalid()
    {
        var profile = Configure();

        Assert.Throws<ReeferThresholdsNotValidException>(
            () => profile.Revise(6m, 2m, 3m, "Bad range", SupervisorUserId, RevisedAtUtc));

        Assert.Equal(-2m, profile.MinimumCelsius);
        Assert.Equal(4m, profile.MaximumCelsius);
        Assert.Equal(1m, profile.TargetCelsius);
        Assert.Equal(ReeferMonitoringProfile.InitialVersion, profile.Version);
        Assert.Equal(ManagerUserId, profile.UpdatedByUserId);
    }

    private static ReeferMonitoringProfile Configure(
        decimal minimumCelsius = -2m,
        decimal maximumCelsius = 4m,
        decimal targetCelsius = 1m)
    {
        return ReeferMonitoringProfile.Configure(
            ContainerId,
            isReefer: true,
            minimumCelsius,
            maximumCelsius,
            targetCelsius,
            ManagerUserId,
            ConfiguredAtUtc);
    }

    private static decimal Parse(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}
