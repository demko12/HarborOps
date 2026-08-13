using System.Globalization;
using HarborOps.Web.Components.Pages.Cargo;

namespace HarborOps.Web.Tests.Components;

public sealed class CargoBookingFormatTests
{
    [Fact]
    public void WeightKg_uses_invariant_formatting_regardless_of_the_current_culture()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("24,000.000", CargoBookingFormat.WeightKg(24000m));
            Assert.Equal("5,000.500", CargoBookingFormat.WeightKg(5000.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void CreatedUtc_uses_invariant_formatting_regardless_of_the_current_culture()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal(
                "2026-08-11 09:42",
                CargoBookingFormat.CreatedUtc(new DateTime(2026, 8, 11, 9, 42, 0, DateTimeKind.Utc)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(true, "Hazardous")]
    [InlineData(false, "Not hazardous")]
    public void HazardLabel_names_the_hazard_state_in_text(bool isHazardous, string expected)
    {
        Assert.Equal(expected, CargoBookingFormat.HazardLabel(isHazardous));
    }

    [Theory]
    [InlineData("Registered", 1)]
    [InlineData("Planned", 2)]
    [InlineData("Loaded", 3)]
    [InlineData("InTransit", 4)]
    [InlineData("Arrived", 5)]
    [InlineData("Unloaded", 6)]
    [InlineData("Completed", 7)]
    [InlineData("intransit", 4)]
    public void LifecycleStep_maps_a_status_to_its_position(string status, int expected)
    {
        Assert.Equal(expected, CargoBookingFormat.LifecycleStep(status));
    }

    [Fact]
    public void LifecycleStep_returns_zero_for_an_unknown_status()
    {
        Assert.Equal(0, CargoBookingFormat.LifecycleStep("Departed"));
    }

    [Fact]
    public void Statuses_match_the_cargo_lifecycle_order()
    {
        Assert.Equal(
            ["Registered", "Planned", "Loaded", "InTransit", "Arrived", "Unloaded", "Completed"],
            CargoBookingFormat.Statuses);
    }

    [Fact]
    public void ShortVoyage_shortens_the_identifier_without_losing_its_ends()
    {
        var voyageId = Guid.Parse("8c7b6a5d-4e3f-4a2b-9c1d-0e9f8a7b6c5d");

        Assert.Equal("8c7b6a5d…6c5d", CargoBookingFormat.ShortVoyage(voyageId));
    }
}
