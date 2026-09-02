using HarborOps.Web.Components.Pages.Crew;

namespace HarborOps.Web.Tests.Components;

public sealed class CrewFormatTests
{
    private static readonly DateOnly Today = new(2026, 8, 17);

    [Fact]
    public void A_passport_that_expired_yesterday_is_flagged()
    {
        var state = Crew.GetPassportState(Today.AddDays(-1), Today);

        Assert.True(state.IsExpired);
        Assert.Equal("passport-expired", state.Class);
    }

    [Fact]
    public void A_passport_expiring_today_is_not_yet_expired()
    {
        var state = Crew.GetPassportState(Today, Today);

        Assert.False(state.IsExpired);
        Assert.Equal(string.Empty, state.Class);
    }

    [Fact]
    public void A_passport_expiring_in_the_future_is_not_flagged()
    {
        var state = Crew.GetPassportState(Today.AddYears(2), Today);

        Assert.False(state.IsExpired);
        Assert.Equal(string.Empty, state.Class);
    }

    [Theory]
    [InlineData("Active", "employment-active")]
    [InlineData("active", "employment-active")]
    [InlineData("ACTIVE", "employment-active")]
    [InlineData("OnLeave", "employment-onleave")]
    [InlineData("onleave", "employment-onleave")]
    [InlineData("Suspended", "employment-suspended")]
    [InlineData("suspended", "employment-suspended")]
    [InlineData("Terminated", "employment-terminated")]
    [InlineData("terminated", "employment-terminated")]
    [InlineData("Unknown", "employment-inactive")]
    public void Employment_status_maps_to_its_badge_modifier(string status, string expected)
    {
        Assert.Equal(expected, Crew.GetEmploymentStatusClass(status));
    }
}
