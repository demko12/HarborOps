using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Voyages;

namespace HarborOps.Web.Tests.Components;

public sealed class VoyageFormatTests
{
    [Fact]
    public void Route_renders_departure_code_to_arrival_code()
    {
        Assert.Equal("MEBAR → ITGOA", Voyages.FormatRoute("MEBAR", "ITGOA"));
    }

    [Fact]
    public void Planned_dates_render_as_utc_without_local_conversion()
    {
        var plannedDeparture = new DateTime(2026, 9, 15, 8, 0, 0, DateTimeKind.Utc);

        Assert.Equal("15 Sep 2026 08:00", Voyages.FormatUtc(plannedDeparture));
    }

    [Fact]
    public void Missing_actual_dates_render_as_a_placeholder()
    {
        Assert.Equal(Voyages.MissingValue, Voyages.FormatUtc(null));
    }

    [Theory]
    [InlineData(Voyages.PlannedStatus, 1, "planned")]
    [InlineData(Voyages.InProgressStatus, 2, "in-progress")]
    [InlineData(Voyages.CompletedStatus, 3, "completed")]
    public void Status_renders_its_lifecycle_step_and_modifier(
        string status,
        int expectedStep,
        string expectedModifier)
    {
        Assert.Equal(expectedStep, Voyages.StatusStep(status));
        Assert.Equal(expectedModifier, Voyages.StatusModifier(status));
    }

    [Fact]
    public void Unknown_status_falls_back_to_the_first_step()
    {
        Assert.Equal(1, Voyages.StatusStep("Cancelled"));
        Assert.Equal("planned", Voyages.StatusModifier("Cancelled"));
    }

    [Fact]
    public void Departure_inside_the_next_day_is_marked_as_imminent()
    {
        var utcNow = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(Voyages.IsImminentDeparture(utcNow, utcNow));
        Assert.True(Voyages.IsImminentDeparture(utcNow.AddHours(12), utcNow));
        Assert.True(Voyages.IsImminentDeparture(
            utcNow.AddHours(Voyages.ImminentDepartureHours),
            utcNow));
    }

    [Fact]
    public void Departure_outside_the_next_day_is_not_marked_as_imminent()
    {
        var utcNow = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

        Assert.False(Voyages.IsImminentDeparture(utcNow.AddMinutes(-1), utcNow));
        Assert.False(Voyages.IsImminentDeparture(
            utcNow.AddHours(Voyages.ImminentDepartureHours).AddMinutes(1),
            utcNow));
    }

    [Fact]
    public void Forbidden_failure_explains_the_missing_permission()
    {
        var failure = new VoyagesApiFailure(
            403,
            "Forbidden",
            "Please review the filters and try again.",
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(Voyages.ForbiddenTitle, Voyages.FailureTitle(failure));
        Assert.Equal(Voyages.ForbiddenMessage, Voyages.FailureMessage(failure));
    }

    [Fact]
    public void Other_failures_keep_the_message_from_the_api()
    {
        var failure = new VoyagesApiFailure(
            0,
            "HarborOps API is unavailable",
            "The voyages board could not reach the API.",
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal("HarborOps API is unavailable", Voyages.FailureTitle(failure));
        Assert.Equal("The voyages board could not reach the API.", Voyages.FailureMessage(failure));
    }

}
