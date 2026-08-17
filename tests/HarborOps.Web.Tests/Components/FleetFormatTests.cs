using HarborOps.Web.Components.Pages.Fleet;

namespace HarborOps.Web.Tests.Components;

public sealed class FleetFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1K")]
    [InlineData(52_000, "52K")]
    [InlineData(999_999, "1000K")]
    [InlineData(1_000_000, "1M")]
    [InlineData(2_500_000, "2.5M")]
    public void Deadweight_is_abbreviated_by_magnitude(decimal dwt, string expected)
    {
        Assert.Equal(expected, Fleet.FormatDwt(dwt));
    }

    [Fact]
    public void Deadweight_keeps_a_single_decimal_for_partial_thousands()
    {
        Assert.Equal("52.5K", Fleet.FormatDwt(52_500));
    }

    [Theory]
    [InlineData("Active", "vessel-status-active")]
    [InlineData("In Transit", "vessel-status-transit")]
    [InlineData("En Route", "vessel-status-transit")]
    [InlineData("At Port", "vessel-status-port")]
    [InlineData("Maintenance", "vessel-status-maintenance")]
    [InlineData("Under Maintenance", "vessel-status-maintenance")]
    [InlineData("Inactive", "vessel-status-neutral")]
    [InlineData("Something else", "vessel-status-neutral")]
    public void Status_maps_to_its_badge_modifier(string status, string expected)
    {
        Assert.Equal(expected, Fleet.GetStatusClass(status));
    }

    [Theory]
    [InlineData("ACTIVE", "vessel-status-active")]
    [InlineData("at port", "vessel-status-port")]
    public void Status_matching_ignores_casing(string status, string expected)
    {
        Assert.Equal(expected, Fleet.GetStatusClass(status));
    }

    [Theory]
    [InlineData("Maintenance", true)]
    [InlineData("Under Maintenance", true)]
    [InlineData("under maintenance", true)]
    [InlineData("Active", false)]
    [InlineData("At Port", false)]
    public void Maintenance_covers_both_wordings_the_api_returns(string status, bool expected)
    {
        Assert.Equal(expected, Fleet.IsMaintenance(status));
    }
}
