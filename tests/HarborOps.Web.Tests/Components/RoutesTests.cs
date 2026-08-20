using HarborOps.Web.Components;
using HarborOps.Web.Components.Pages;
using AdministrationPage = HarborOps.Web.Components.Pages.Administration.Administration;
using CargoBookingDetailsPage = HarborOps.Web.Components.Pages.Cargo.CargoBookingDetails;
using CargoBookingsPage = HarborOps.Web.Components.Pages.Cargo.CargoBookings;
using CrewPage = HarborOps.Web.Components.Pages.Crew.Crew;
using FleetPage = HarborOps.Web.Components.Pages.Fleet.Fleet;
using ReportsPage = HarborOps.Web.Components.Pages.Reports.Reports;

namespace HarborOps.Web.Tests.Components;

public sealed class RoutesTests
{
    [Theory]
    [InlineData(typeof(NotFound))]
    [InlineData(typeof(Error))]
    public void Recovery_pages_render_without_an_authentication_check(Type pageType)
    {
        Assert.Contains(pageType, Routes.AnonymousPages);
    }

    [Fact]
    public void Signing_in_is_the_only_other_page_reachable_without_authentication()
    {
        Assert.Equal(
            [typeof(Login), typeof(Error), typeof(NotFound)],
            Routes.AnonymousPages);
    }

    [Theory]
    [InlineData(typeof(FleetPage))]
    [InlineData(typeof(CrewPage))]
    [InlineData(typeof(CargoBookingsPage))]
    [InlineData(typeof(CargoBookingDetailsPage))]
    [InlineData(typeof(ReportsPage))]
    [InlineData(typeof(AdministrationPage))]
    [InlineData(typeof(AccessDenied))]
    public void Application_pages_stay_behind_the_authentication_check(Type pageType)
    {
        Assert.DoesNotContain(pageType, Routes.AnonymousPages);
    }
}
