using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Cargo;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class CargoPageTests : BunitContext
{
    private const string VoyageId = "30000000-0000-0000-0000-000000000002";
    private const string RegisteredBookingId = "20000000-0000-0000-0000-000000000001";
    private const string RegisteredBooking = $$"""{"id":"{{RegisteredBookingId}}"}""";

    private readonly List<Uri> requestedUris = [];
    private readonly List<Uri> voyageRequestedUris = [];
    private readonly List<string> postedBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, Page());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToVoyages =
        _ => Json(HttpStatusCode.OK, VoyagePage(Voyage("VOY-2026-014")));

    [Fact]
    public void Hazardous_rows_are_marked_with_text_and_a_placard_not_colour_alone()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(
            Row("BK-0001", isHazardous: true),
            Row("BK-0002", isHazardous: false)));

        var page = RenderCargoPage();

        var hazardChips = page.FindAll(".cargo-hazard-chip");
        var hazardRows = page.FindAll("tr.cargo-hazard-row");

        Assert.Single(hazardChips);
        Assert.Single(hazardRows);
        Assert.Contains("Hazardous", hazardChips[0].TextContent, StringComparison.Ordinal);
        Assert.Single(hazardChips[0].QuerySelectorAll(".cargo-placard"));
        Assert.Contains("Not hazardous", page.Find(".cargo-no-hazard").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Weight_is_rendered_in_invariant_kilograms()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("BK-0001", weightKg: 24000.5m)));

        var page = RenderCargoPage();

        Assert.Contains("24,000.500", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Searching_sends_the_search_term_to_the_api()
    {
        var page = RenderCargoPage();

        page.Find("#cargo-search").Input("ammonium");

        page.WaitForAssertion(() =>
            Assert.Contains(
                requestedUris,
                uri => uri.Query.Contains("search=ammonium", StringComparison.Ordinal)));
    }

    [Fact]
    public void Changing_the_status_and_hazardous_filters_sends_them_to_the_api()
    {
        var page = RenderCargoPage();

        page.Find("#cargo-status").Change("InTransit");
        page.Find("#cargo-hazardous").Change(true);

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris[^1].Query;

            Assert.Contains("status=InTransit", lastQuery, StringComparison.Ordinal);
            Assert.Contains("isHazardous=true", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Clearing_the_filters_returns_to_an_unfiltered_first_page()
    {
        var page = RenderCargoPage();

        page.Find("#cargo-search").Input("ammonium");
        page.WaitForAssertion(() => Assert.Equal(2, requestedUris.Count));

        page.Find(".cargo-reset").Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris[^1].Query;

            Assert.DoesNotContain("search=", lastQuery, StringComparison.Ordinal);
            Assert.DoesNotContain("status=", lastQuery, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Empty_data_shows_the_empty_state_instead_of_a_table()
    {
        respond = _ => Json(HttpStatusCode.OK, Page());

        var page = RenderCargoPage();

        Assert.Empty(page.FindAll("table.cargo-table"));
        Assert.Contains("No bookings match your filters", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreachable_api_shows_the_error_state_with_a_retry_action()
    {
        respond = _ => throw new HttpRequestException("Connection failed.");

        var page = RenderCargoPage();

        Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("table.cargo-table"));
        Assert.NotNull(page.Find(".api-error-state button"));
    }

    [Fact]
    public void Retry_reloads_the_bookings_after_a_failure()
    {
        var attempt = 0;
        respond = _ =>
        {
            attempt++;

            return attempt == 1
                ? throw new HttpRequestException("Connection failed.")
                : Json(HttpStatusCode.OK, Page(Row("BK-0007")));
        };

        var page = RenderCargoPage();

        Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal);

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("BK-0007", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_problems_are_listed_above_the_results()
    {
        respond = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"status":["Status is not a known cargo status."]}}""");

        var page = RenderCargoPage();

        Assert.Contains("Status is not a known cargo status.", page.Find(".cargo-validation").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".api-error-state"));
    }

    [Fact]
    public void Paging_requests_the_next_page_and_disables_previous_on_the_first_page()
    {
        respond = request =>
        {
            var pageNumber = request.RequestUri!.Query.Contains("pageNumber=2", StringComparison.Ordinal) ? 2 : 1;

            return Json(HttpStatusCode.OK, Page(pageNumber, 60, Row($"BK-PAGE-{pageNumber}")));
        };

        var page = RenderCargoPage();

        var previous = page.FindAll(".cargo-pages button")[0];

        Assert.True(previous.HasAttribute("disabled"));
        Assert.Contains("Showing 1–1 of 60 bookings", page.Markup, StringComparison.Ordinal);

        page.FindAll(".cargo-pages button")[1].Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("BK-PAGE-2", page.Markup, StringComparison.Ordinal);
            Assert.Contains("pageNumber=2", requestedUris[^1].Query, StringComparison.Ordinal);
            Assert.False(page.FindAll(".cargo-pages button")[0].HasAttribute("disabled"));
        });
    }

    [Fact]
    public void The_status_column_shows_the_position_in_the_cargo_lifecycle()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("BK-0001", status: "Loaded")));

        var page = RenderCargoPage();

        Assert.Equal(7, page.FindAll(".cargo-track i").Count);
        Assert.Equal(3, page.FindAll(".cargo-track i.on").Count);
    }



    [Fact]
    public void The_register_form_stays_closed_until_it_is_asked_for()
    {
        var page = RenderCargoPage();

        Assert.Empty(page.FindAll(".cargo-modal"));

        page.Find("#cargo-register-toggle").Click();

        var dialog = page.Find(".cargo-modal");

        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
    }

    [Fact]
    public void Cancelling_the_register_dialog_discards_what_was_typed()
    {
        var page = RenderCargoPage();

        page.Find("#cargo-register-toggle").Click();
        page.Find("#cargo-booking-number").Change("BK-2026-0145");
        page.Find(".cargo-modal-cancel").Click();

        Assert.Empty(page.FindAll(".cargo-modal"));

        page.Find("#cargo-register-toggle").Click();

        Assert.True(string.IsNullOrEmpty(page.Find("#cargo-booking-number").GetAttribute("value")));
    }

    [Fact]
    public void Clicking_the_backdrop_closes_the_register_dialog()
    {
        var page = RenderCargoPage();

        page.Find("#cargo-register-toggle").Click();
        page.Find(".cargo-modal-backdrop").Click();

        Assert.Empty(page.FindAll(".cargo-modal"));
    }
    [Fact]
    public void The_voyage_picker_offers_only_voyages_that_can_still_load_cargo()
    {
        var page = RenderCargoPage();

        page.Find("#cargo-register-toggle").Click();
        page.Find("#cargo-voyage").Focus();

        page.WaitForAssertion(() => Assert.NotEmpty(voyageRequestedUris));

        var query = voyageRequestedUris[0].Query;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Cargo is loaded before departure, so a sailed voyage can never carry a new booking.
        Assert.Contains("status=Planned", query, StringComparison.Ordinal);

        // Overdue planned voyages sit before the window and are the likeliest urgent bookings.
        Assert.Contains("includeActive=true", query, StringComparison.Ordinal);
        Assert.Contains($"from={today}", query, StringComparison.Ordinal);
    }

    [Fact]
    public void Searching_the_voyage_picker_sends_the_voyage_number()
    {
        var page = RenderCargoPage();

        page.Find("#cargo-register-toggle").Click();
        page.Find("#cargo-voyage").Input("VOY-2026");

        page.WaitForAssertion(() =>
            Assert.Contains(
                voyageRequestedUris,
                uri => uri.Query.Contains("search=VOY-2026", StringComparison.Ordinal)));
    }

    [Fact]
    public void Registering_a_booking_posts_the_selected_voyage_and_opens_it()
    {
        respond = CaptureAndRespond(HttpStatusCode.Created, RegisteredBooking);

        var page = RenderCargoPage();

        FillRegisterForm(page);
        page.Find(".cargo-register-submit").Click();

        page.WaitForAssertion(() => Assert.Single(postedBodies));

        Assert.Contains("\"bookingNumber\":\"BK-2026-0145\"", postedBodies[0], StringComparison.Ordinal);
        Assert.Contains($"\"voyageId\":\"{VoyageId}\"", postedBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"isHazardous\":true", postedBodies[0], StringComparison.Ordinal);

        // The API hands back the new booking's identifier, so the operator continues on it
        // instead of hunting for the row under whatever filter the board still carries.
        page.WaitForAssertion(() =>
            Assert.EndsWith(
                $"/cargo/{RegisteredBookingId}",
                Services.GetRequiredService<NavigationManager>().Uri,
                StringComparison.Ordinal));

        Assert.Empty(page.FindAll(".cargo-register-submit"));
    }

    [Fact]
    public void The_registered_booking_carries_the_active_filters_so_it_can_return_to_them()
    {
        respond = CaptureAndRespond(HttpStatusCode.Created, RegisteredBooking);

        var page = RenderCargoPageAt("/cargo?status=Registered&hazardous=true");

        FillRegisterForm(page);
        page.Find(".cargo-register-submit").Click();

        page.WaitForAssertion(() =>
        {
            var address = Services.GetRequiredService<NavigationManager>().Uri;

            Assert.Contains($"/cargo/{RegisteredBookingId}?from=", address, StringComparison.Ordinal);
            Assert.Contains("status%3DRegistered", address, StringComparison.Ordinal);
            Assert.Contains("hazardous%3Dtrue", address, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_duplicate_booking_number_is_reported_without_losing_the_form()
    {
        respond = CaptureAndRespond(
            HttpStatusCode.Conflict,
            """{"title":"A cargo booking with this booking number already exists.","status":409,"errors":{"bookingNumber":["Booking number BK-2026-0145 is already used."]}}""");

        var page = RenderCargoPage();

        FillRegisterForm(page);
        page.Find(".cargo-register-submit").Click();

        page.WaitForAssertion(() =>
            Assert.Contains(
                "Booking number BK-2026-0145 is already used.",
                page.Markup,
                StringComparison.Ordinal));

        Assert.Equal("BK-2026-0145", page.Find("#cargo-booking-number").GetAttribute("value"));
    }

    [Fact]
    public void An_unreachable_api_reports_the_registration_failure()
    {
        respond = CaptureAndRespond(HttpStatusCode.ServiceUnavailable, "not json");

        var page = RenderCargoPage();

        FillRegisterForm(page);
        page.Find(".cargo-register-submit").Click();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".cargo-register-error")));
        Assert.Contains("Unable to register the cargo booking", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_booking_without_a_voyage_is_not_sent_to_the_api()
    {
        respond = CaptureAndRespond(HttpStatusCode.Created, RegisteredBooking);

        var page = RenderCargoPage();

        FillRegisterForm(page, selectVoyage: false);
        page.Find(".cargo-register-submit").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("Select the voyage this cargo sails on.", page.Markup, StringComparison.Ordinal));
        Assert.Empty(postedBodies);
    }

    [Fact]
    public void The_register_form_is_hidden_without_the_cargo_manage_permission()
    {
        var page = RenderCargoPage(CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Empty(page.FindAll("#cargo-register-toggle"));
        Assert.Empty(page.FindAll(".cargo-register-submit"));
    }


    [Fact]
    public void A_booking_links_to_its_details_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("BK-0001")));

        var page = RenderCargoPage();

        Assert.Equal(
            "/cargo/20000000-0000-0000-0000-000000000001",
            page.Find(".cargo-booking-no").GetAttribute("href"));
    }

    [Fact]
    public void A_booking_link_carries_the_active_filters_so_details_can_return_to_them()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("BK-0001")));

        var page = RenderCargoPage();
        page.Find("#cargo-status").Change("Loaded");

        page.WaitForAssertion(() =>
            Assert.Contains(
                "from=",
                page.Find(".cargo-booking-no").GetAttribute("href")!,
                StringComparison.Ordinal));

        var href = page.Find(".cargo-booking-no").GetAttribute("href")!;
        var boardQuery = Uri.UnescapeDataString(href[(href.IndexOf("from=", StringComparison.Ordinal) + 5)..]);

        Assert.Equal("status=Loaded", boardQuery);
    }

    [Fact]
    public void Applying_a_filter_puts_it_in_the_board_address()
    {
        var page = RenderCargoPage();

        page.Find("#cargo-status").Change("InTransit");
        page.Find("#cargo-hazardous").Change(true);

        page.WaitForAssertion(() =>
        {
            var address = Services.GetRequiredService<NavigationManager>().Uri;

            Assert.Contains("status=InTransit", address, StringComparison.Ordinal);
            Assert.Contains("hazardous=true", address, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_board_opens_on_the_filters_carried_in_its_address()
    {
        RenderCargoPageAt("/cargo?search=steel&status=Loaded&hazardous=true&page=2");

        var query = requestedUris[0].Query;

        Assert.Contains("search=steel", query, StringComparison.Ordinal);
        Assert.Contains("status=Loaded", query, StringComparison.Ordinal);
        Assert.Contains("isHazardous=true", query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", query, StringComparison.Ordinal);
    }

    [Fact]
    public void Clearing_the_filters_empties_the_board_address()
    {
        var page = RenderCargoPageAt("/cargo?search=steel&status=Loaded");
        page.Find(".cargo-reset").Click();

        page.WaitForAssertion(() =>
        {
            var address = Services.GetRequiredService<NavigationManager>().Uri;

            Assert.DoesNotContain("search=", address, StringComparison.Ordinal);
            Assert.DoesNotContain("status=", address, StringComparison.Ordinal);
        });
    }
    private void FillRegisterForm(
        IRenderedComponent<CargoBookings> page,
        bool selectVoyage = true)
    {
        page.Find("#cargo-register-toggle").Click();

        if (selectVoyage)
        {
            page.Find("#cargo-voyage").Focus();
            page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".voyage-picker-option")));
            page.Find(".voyage-picker-option").Click();
        }

        page.Find("#cargo-booking-number").Change("BK-2026-0145");
        page.Find("#cargo-description").Change("Ammonium nitrate");
        page.Find("#cargo-cargo-type").Change("Bulk");
        page.Find("#cargo-weight").Change("24000.5");
        page.Find("#cargo-packages").Change("10");
        page.Find("#cargo-is-hazardous").Change(true);
    }

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> CaptureAndRespond(
        HttpStatusCode statusCode,
        string json)
    {
        return async request =>
        {
            if (request.Method != HttpMethod.Post)
            {
                return await Json(HttpStatusCode.OK, Page());
            }

            postedBodies.Add(await request.Content!.ReadAsStringAsync(CancellationToken.None));

            return await Json(statusCode, json);
        };
    }

    private static string Voyage(string voyageNumber)
    {
        return $$"""
            {"voyageId":"{{VoyageId}}","voyageNumber":"{{voyageNumber}}","vesselId":"40000000-0000-0000-0000-000000000004","departurePortCode":"MEBAR","arrivalPortCode":"ITGOA","status":"Planned","plannedDeparture":"2026-09-02T06:00:00Z","plannedArrival":"2026-09-06T18:00:00Z","actualDeparture":null,"actualArrival":null}
            """;
    }

    private static string VoyagePage(params string[] voyages)
    {
        return $$"""
            {"items":[{{string.Join(',', voyages)}}],"pageNumber":1,"pageSize":25,"totalCount":{{voyages.Length}}}
            """;
    }
    private IRenderedComponent<CargoBookings> RenderCargoPage(
        params string[] permissions)
    {
        return RenderCargoPageAt(null, permissions);
    }

    private IRenderedComponent<CargoBookings> RenderCargoPageAt(
        string? boardAddress,
        params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var voyagesClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            voyageRequestedUris.Add(request.RequestUri!);

            return respondToVoyages(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CargoBookingsApiClient(httpClient));
        Services.AddSingleton(new VoyagesApiClient(voyagesClient));
        ConfigureAuthorization(permissions.Length == 0
            ? [CurrentUserAuthorizationState.CargoManagePermission]
            : permissions);

        if (boardAddress is not null)
        {
            // The board reads its filters from the address, so set it before the first render.
            Services.GetRequiredService<NavigationManager>().NavigateTo(boardAddress);
        }

        var page = Render<CargoBookings>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private void ConfigureAuthorization(params string[] permissions)
    {
        Services.RemoveAll<IHttpContextAccessor>();
        Services.RemoveAll<CurrentUserAuthorizationState>();
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    permissions
                        .Select(permission => new Claim(
                            CurrentUserAuthorizationState.PermissionClaimType,
                            permission))
                        .ToArray(),
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();
    }

    private static string Row(
        string bookingNumber,
        bool isHazardous = false,
        decimal weightKg = 5000m,
        string status = "Registered")
    {
        return $$"""
            {"cargoBookingId":"20000000-0000-0000-0000-000000000001","bookingNumber":"{{bookingNumber}}","voyageId":"30000000-0000-0000-0000-000000000002","description":"Steel coils","cargoType":"General Cargo","weightKg":{{weightKg.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"packageCount":10,"isHazardous":{{(isHazardous ? "true" : "false")}},"status":"{{status}}","createdUtc":"2026-08-11T09:42:00Z"}
            """;
    }

    private static string Page(params string[] rows)
    {
        return Page(1, rows.Length, rows);
    }

    private static string Page(int pageNumber, int totalCount, params string[] rows)
    {
        return $$"""
            {"items":[{{string.Join(',', rows)}}],"pageNumber":{{pageNumber}},"pageSize":25,"totalCount":{{totalCount}}}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request);
        }
    }
}
