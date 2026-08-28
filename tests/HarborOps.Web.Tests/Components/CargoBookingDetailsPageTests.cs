using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DetailsPage = HarborOps.Web.Components.Pages.Cargo.CargoBookingDetails;

namespace HarborOps.Web.Tests.Components;

public sealed class CargoBookingDetailsPageTests : BunitContext
{
    private const string BookingId = "20000000-0000-0000-0000-000000000001";
    private const string VoyageId = "30000000-0000-0000-0000-000000000002";
    private const string ResponsibleUserId = "50000000-0000-0000-0000-000000000005";
    private const string ContainerId = "70000000-0000-0000-0000-000000000006";
    private const string SecondContainerId = "70000000-0000-0000-0000-000000000016";
    private const string OperationId = "80000000-0000-0000-0000-000000000007";
    private const string SecondOperationId = "80000000-0000-0000-0000-000000000017";
    private const string StartedStamp = "\"2026-08-12T07:30:00Z\"";
    private const string FinishedStamp = "\"2026-08-12T09:15:00Z\"";
    private const string EmptyItems = """{"items":[]}""";

    private const string DeclaredSafetyInfo = """
        {"cargoSafetyInfoId":"60000000-0000-0000-0000-000000000006","imdgClass":"5.1","unNumber":"UN1942","storageConditions":"Keep below 25C","handlingNotes":"No shock loading"}
        """;

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, Details());

    public CargoBookingDetailsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_details_are_requested_for_the_booking_in_the_route()
    {
        RenderDetailsPage();

        Assert.Equal($"/api/cargo/bookings/{BookingId}", requestedUris[0].AbsolutePath);
    }

    [Fact]
    public void Every_booking_field_is_shown()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(
            bookingNumber: "BK-2026-0145",
            description: "Ammonium nitrate",
            cargoType: "Bulk",
            weightKg: "24000.5",
            packageCount: 10,
            status: "Loaded"));

        var page = RenderDetailsPage();

        Assert.Contains("BK-2026-0145", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Ammonium nitrate", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Bulk", page.Markup, StringComparison.Ordinal);
        Assert.Contains("24,000.500", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Loaded", page.Find(".cargo-status").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_lifecycle_track_shows_the_position_in_the_cargo_lifecycle()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(status: "InTransit"));

        var page = RenderDetailsPage();

        Assert.Equal(7, page.FindAll(".cargo-track i").Count);
        Assert.Equal(4, page.FindAll(".cargo-track i.on").Count);
    }

    [Fact]
    public void Hazardous_cargo_is_marked_with_text_and_a_placard_not_colour_alone()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(isHazardous: true));

        var page = RenderDetailsPage();

        var chip = page.Find(".cargo-hazard-chip");

        Assert.Contains("Hazardous", chip.TextContent, StringComparison.Ordinal);
        Assert.Single(chip.QuerySelectorAll(".cargo-placard"));
    }

    [Fact]
    public void Non_hazardous_cargo_reports_that_no_declaration_is_required()
    {
        var page = RenderDetailsPage();

        Assert.Contains("Not hazardous", page.Find(".booking-no-hazard").TextContent, StringComparison.Ordinal);
        Assert.Contains("no IMDG declaration requirement", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".cargo-hazard-chip"));
    }

    [Fact]
    public void Declared_safety_information_is_shown()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(isHazardous: true, safetyInfo: DeclaredSafetyInfo));

        var page = RenderDetailsPage();

        Assert.Contains("5.1", page.Markup, StringComparison.Ordinal);
        Assert.Contains("UN1942", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Keep below 25C", page.Markup, StringComparison.Ordinal);
        Assert.Contains("No shock loading", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Hazardous_cargo_without_a_declaration_says_so()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(isHazardous: true));

        var page = RenderDetailsPage();

        Assert.Contains(
            "Safety information has not been declared",
            page.Find(".booking-note-warning").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Tracking_events_are_listed_in_the_order_the_api_returned_them()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(trackingEvents:
        [
            TrackingEvent(1, "Planned", "2026-08-12T07:00:00Z", "MEBAR"),
            TrackingEvent(2, "Loaded", "2026-08-13T11:30:00Z", "MEBAR berth 4"),
        ]));

        var page = RenderDetailsPage();

        var entries = page.FindAll(".booking-event-row");

        Assert.Equal(2, entries.Count);
        Assert.Contains("Planned", entries[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("2026-08-12", entries[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Loaded", entries[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("MEBAR berth 4", entries[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_responsible_user_is_shown_as_an_identifier_without_a_name_lookup()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(trackingEvents:
        [
            TrackingEvent(1, "Planned", "2026-08-12T07:00:00Z", "MEBAR"),
        ]));

        var page = RenderDetailsPage();

        Assert.Equal(ResponsibleUserId, page.Find(".booking-event-actor").GetAttribute("title"));
        Assert.DoesNotContain(requestedUris, uri => uri.AbsolutePath.Contains("identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_booking_without_tracking_events_shows_the_empty_state()
    {
        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".booking-event-row"));
        Assert.Contains("No tracking events yet", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_booking_reports_that_it_is_gone_without_offering_a_retry()
    {
        respond = _ => Json(
            HttpStatusCode.NotFound,
            """
            {"title":"Cargo booking not found.","status":404,"detail":"No cargo booking was found."}
            """);

        var page = RenderDetailsPage();

        Assert.Contains("Booking not found", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".api-error-state"));
    }

    [Fact]
    public void A_forbidden_read_explains_the_missing_permission()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, "{}");

        var page = RenderDetailsPage();

        Assert.Contains("You cannot view this booking", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".api-error-state"));
    }

    [Fact]
    public void An_unreachable_api_shows_the_error_state_with_a_retry_action()
    {
        respond = _ => throw new HttpRequestException("offline");

        var page = RenderDetailsPage();

        Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal);
        Assert.Single(page.FindAll(".api-error-state"));
    }

    [Fact]
    public void Retry_reloads_the_booking_after_a_failure()
    {
        var attempts = 0;
        respond = _ =>
        {
            attempts++;

            return attempts == 1
                ? throw new HttpRequestException("offline")
                : Json(HttpStatusCode.OK, Details(bookingNumber: "BK-2026-0145"));
        };

        var page = RenderDetailsPage();
        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("BK-2026-0145", page.Markup, StringComparison.Ordinal));
        Assert.Equal(2, DetailsRequestCount());
    }

    [Fact]
    public void The_page_uses_the_cargo_breadcrumb_without_a_duplicate_back_link()
    {
        var page = RenderDetailsPage();

        Assert.Equal("/cargo", page.Find(".page-breadcrumb a").GetAttribute("href"));
        Assert.Empty(page.FindAll(".booking-back"));
    }

    [Fact]
    public void The_cargo_breadcrumb_preserves_the_board_filters()
    {
        var page = RenderDetailsPageFrom("search=steel&status=Loaded&page=2");

        Assert.Equal("/cargo?search=steel&status=Loaded&page=2", page.Find(".page-breadcrumb a").GetAttribute("href"));
    }


    [Fact]
    public void The_safety_form_is_offered_for_hazardous_cargo_without_a_declaration()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(isHazardous: true));

        var page = RenderDetailsPage();

        Assert.Single(page.FindAll(".safety-submit"));
    }

    [Fact]
    public void The_safety_form_is_hidden_for_non_hazardous_cargo()
    {
        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".safety-submit"));
    }

    [Fact]
    public void The_safety_form_is_hidden_once_a_declaration_exists()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(isHazardous: true, safetyInfo: DeclaredSafetyInfo));

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".safety-submit"));
    }

    [Fact]
    public void The_safety_form_is_hidden_without_the_cargo_manage_permission()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(isHazardous: true));

        var page = RenderDetailsPage(CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Empty(page.FindAll(".safety-submit"));
        Assert.Contains("Safety information has not been declared", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Declaring_safety_information_posts_to_the_booking_safety_route()
    {
        var postedBodies = new List<string>();
        respond = async request =>
        {
            if (request.Method != HttpMethod.Post)
            {
                return await Json(
                    HttpStatusCode.OK,
                    Details(isHazardous: true, safetyInfo: postedBodies.Count == 0 ? null : DeclaredSafetyInfo));
            }

            postedBodies.Add(await request.Content!.ReadAsStringAsync(CancellationToken.None));

            return await Json(
                HttpStatusCode.Created,
                """
                {"id":"60000000-0000-0000-0000-000000000006"}
                """);
        };

        var page = RenderDetailsPage();
        FillSafetyForm(page);
        page.Find(".safety-submit").Click();

        page.WaitForAssertion(() => Assert.Single(postedBodies));

        Assert.Equal(
            $"/api/cargo/bookings/{BookingId}/safety-information",
            requestedUris.Single(uri => uri.AbsolutePath.EndsWith("safety-information", StringComparison.Ordinal)).AbsolutePath);
        Assert.Contains("\"imdgClass\":\"5.1\"", postedBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"unNumber\":\"UN1942\"", postedBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"storageConditions\":\"Keep below 25C\"", postedBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_declaration_replaces_the_form_with_the_reloaded_safety_information()
    {
        var declared = false;
        respond = request =>
        {
            if (request.Method != HttpMethod.Post)
            {
                return Json(
                    HttpStatusCode.OK,
                    Details(isHazardous: true, safetyInfo: declared ? DeclaredSafetyInfo : null));
            }

            declared = true;

            return Json(
                HttpStatusCode.Created,
                """
                {"id":"60000000-0000-0000-0000-000000000006"}
                """);
        };

        var page = RenderDetailsPage();
        FillSafetyForm(page);
        page.Find(".safety-submit").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".safety-submit")));
        Assert.Contains("UN1942", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_incomplete_declaration_is_not_sent_to_the_api()
    {
        var posts = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posts++;
            }

            return Json(HttpStatusCode.OK, Details(isHazardous: true));
        };

        var page = RenderDetailsPage();
        page.Find(".safety-submit").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("IMDG class is required.", page.Markup, StringComparison.Ordinal));
        Assert.Equal(0, posts);
    }

    [Fact]
    public void A_rejected_declaration_reports_the_error_on_its_field()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """
                {"title":"One or more validation errors occurred.","status":400,"errors":{"unNumber":["UN number must not exceed 10 characters."]}}
                """)
            : Json(HttpStatusCode.OK, Details(isHazardous: true));

        var page = RenderDetailsPage();
        FillSafetyForm(page);
        page.Find(".safety-submit").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("UN number must not exceed 10 characters.", page.Markup, StringComparison.Ordinal));
        Assert.Single(page.FindAll(".safety-submit"));
    }

    [Fact]
    public void A_conflict_from_the_api_is_reported_above_the_form()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Conflict,
                """
                {"title":"Cargo booking already has safety information.","status":409,"detail":"Safety information was already declared."}
                """)
            : Json(HttpStatusCode.OK, Details(isHazardous: true));

        var page = RenderDetailsPage();
        FillSafetyForm(page);
        page.Find(".safety-submit").Click();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".booking-form-error")));
        Assert.Contains(
            "Cargo booking already has safety information.",
            page.Find(".booking-form-error").TextContent,
            StringComparison.Ordinal);
    }


    [Fact]
    public void Only_the_next_status_from_the_api_is_offered()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(status: "Loaded", nextStatus: "InTransit"));

        var page = RenderDetailsPage();

        Assert.Contains("Record InTransit", page.Find(".tracking-submit").TextContent, StringComparison.Ordinal);

        // No status picker exists, so no other step in the vocabulary can be requested.
        Assert.Empty(page.FindAll("select:not(#operation-type)"));
        Assert.DoesNotContain("Record Completed", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_completed_booking_offers_no_tracking_action()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(status: "Completed", nextStatus: null));

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".tracking-submit"));
        Assert.Contains(
            "Lifecycle complete",
            page.Find(".booking-complete").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_tracking_action_is_hidden_without_the_cargo_manage_permission()
    {
        var page = RenderDetailsPage(CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Empty(page.FindAll(".tracking-submit"));
    }

    [Fact]
    public void Recording_a_tracking_event_posts_the_next_status_in_utc()
    {
        var postedBodies = new List<string>();
        respond = async request =>
        {
            if (request.Method != HttpMethod.Post)
            {
                return await Json(HttpStatusCode.OK, Details(status: "Loaded", nextStatus: "InTransit"));
            }

            postedBodies.Add(await request.Content!.ReadAsStringAsync(CancellationToken.None));

            return await Json(
                HttpStatusCode.Created,
                """
                {"id":"70000001-0000-0000-0000-000000000007","status":"InTransit"}
                """);
        };

        var page = RenderDetailsPage();
        page.Find("#tracking-occurred").Change("2026-08-14T10:15:00");
        page.Find("#tracking-location").Change("Suez Canal");
        page.Find(".tracking-submit").Click();

        page.WaitForAssertion(() => Assert.Single(postedBodies));

        Assert.Equal(
            $"/api/cargo/bookings/{BookingId}/tracking-events",
            requestedUris.Single(uri => uri.AbsolutePath.EndsWith("tracking-events", StringComparison.Ordinal)).AbsolutePath);
        Assert.Contains("\"status\":\"InTransit\"", postedBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"location\":\"Suez Canal\"", postedBodies[0], StringComparison.Ordinal);

        // The API rejects a timestamp that does not state UTC, so the payload must carry the Z.
        Assert.Contains("2026-08-14T10:15:00Z", postedBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_recorded_event_reloads_the_booking_and_offers_the_following_step()
    {
        var recorded = false;
        respond = request =>
        {
            if (request.Method != HttpMethod.Post)
            {
                return recorded
                    ? Json(HttpStatusCode.OK, Details(
                        status: "InTransit",
                        nextStatus: "Arrived",
                        trackingEvents: [TrackingEvent(1, "InTransit", "2026-08-14T10:15:00Z", "Suez Canal")]))
                    : Json(HttpStatusCode.OK, Details(status: "Loaded", nextStatus: "InTransit"));
            }

            recorded = true;

            return Json(
                HttpStatusCode.Created,
                """
                {"id":"70000001-0000-0000-0000-000000000007","status":"InTransit"}
                """);
        };

        var page = RenderDetailsPage();
        page.Find("#tracking-occurred").Change("2026-08-14T10:15:00");
        page.Find("#tracking-location").Change("Suez Canal");
        page.Find(".tracking-submit").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("Record Arrived", page.Find(".tracking-submit").TextContent, StringComparison.Ordinal));
        Assert.Single(page.FindAll(".booking-event-row"));
    }

    [Fact]
    public void An_event_without_a_location_is_not_sent_to_the_api()
    {
        var posts = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posts++;
            }

            return Json(HttpStatusCode.OK, Details(status: "Loaded", nextStatus: "InTransit"));
        };

        var page = RenderDetailsPage();
        page.Find(".tracking-submit").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("Location is required.", page.Markup, StringComparison.Ordinal));
        Assert.Equal(0, posts);
    }

    [Fact]
    public void A_rejected_transition_is_reported_and_the_authoritative_status_is_reloaded()
    {
        var attempts = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json(
                    HttpStatusCode.Conflict,
                    """
                    {"title":"Cargo status transition is not allowed.","status":409,"detail":"Loaded cannot advance to InTransit twice."}
                    """);
            }

            if (!request.RequestUri!.AbsolutePath.EndsWith(BookingId, StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{"items":[]}""");
            }

            attempts++;

            // Someone else advanced the booking while this page was open.
            return attempts == 1
                ? Json(HttpStatusCode.OK, Details(status: "Loaded", nextStatus: "InTransit"))
                : Json(HttpStatusCode.OK, Details(status: "InTransit", nextStatus: "Arrived"));
        };

        var page = RenderDetailsPage();
        page.Find("#tracking-occurred").Change("2026-08-14T10:15:00");
        page.Find("#tracking-location").Change("Suez Canal");
        page.Find(".tracking-submit").Click();

        page.WaitForAssertion(() =>
            Assert.Contains(
                "Cargo status transition is not allowed.",
                page.Find(".booking-form-error").TextContent,
                StringComparison.Ordinal));
        Assert.Equal(2, attempts);
        Assert.Contains("Record Arrived", page.Find(".tracking-submit").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rejected_timestamp_is_reported_on_its_field()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """
                {"title":"One or more validation errors occurred.","status":400,"errors":{"occurredUtc":["Occurred UTC cannot be in the future."]}}
                """)
            : Json(HttpStatusCode.OK, Details(status: "Loaded", nextStatus: "InTransit"));

        var page = RenderDetailsPage();
        page.Find("#tracking-occurred").Change("2026-08-14T10:15:00");
        page.Find("#tracking-location").Change("Suez Canal");
        page.Find(".tracking-submit").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("Occurred UTC cannot be in the future.", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Undeclared_hazardous_cargo_cannot_be_loaded_from_the_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(
            isHazardous: true,
            status: "Planned",
            nextStatus: "Loaded"));

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".tracking-submit"));
        Assert.Contains(
            "Declare the safety information before this hazardous cargo can be loaded.",
            page.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Declared_hazardous_cargo_can_be_loaded_from_the_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(
            isHazardous: true,
            status: "Planned",
            nextStatus: "Loaded",
            safetyInfo: DeclaredSafetyInfo));

        var page = RenderDetailsPage();

        Assert.Contains("Record Loaded", page.Find(".tracking-submit").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Undeclared_hazardous_cargo_can_still_be_planned_from_the_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(
            isHazardous: true,
            status: "Registered",
            nextStatus: "Planned"));

        var page = RenderDetailsPage();

        Assert.Contains("Record Planned", page.Find(".tracking-submit").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Undeclared_non_hazardous_cargo_can_be_loaded_from_the_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(status: "Planned", nextStatus: "Loaded"));

        var page = RenderDetailsPage();

        Assert.Contains("Record Loaded", page.Find(".tracking-submit").TextContent, StringComparison.Ordinal);
    }

    private static void FillSafetyForm(IRenderedComponent<DetailsPage> page)
    {
        page.Find("#safety-imdg-class").Change("5.1");
        page.Find("#safety-un-number").Change("UN1942");
        page.Find("#safety-storage").Change("Keep below 25C");
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

    private IRenderedComponent<DetailsPage> RenderDetailsPage(params string[] permissions)
    {
        return RenderDetailsPageFrom(null, permissions);
    }

    private IRenderedComponent<DetailsPage> RenderDetailsPageFrom(
        string? boardQuery,
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

        Services.AddSingleton(new CargoBookingsApiClient(httpClient));
        ConfigureAuthorization(permissions.Length == 0
            ? [CurrentUserAuthorizationState.CargoManagePermission]
            : permissions);

        // A query-string parameter can only reach the component through the address bar.
        Services.GetRequiredService<NavigationManager>().NavigateTo(boardQuery is null
            ? $"/cargo/{BookingId}"
            : $"/cargo/{BookingId}?from={Uri.EscapeDataString(boardQuery)}");

        var page = Render<DetailsPage>(parameters => parameters
            .Add(component => component.BookingId, Guid.Parse(BookingId)));
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private static string TrackingEvent(int ordinal, string status, string occurredUtc, string location)
    {
        return $$"""
            {"cargoTrackingEventId":"7000000{{ordinal}}-0000-0000-0000-000000000007","status":"{{status}}","occurredUtc":"{{occurredUtc}}","location":"{{location}}","responsibleUserId":"{{ResponsibleUserId}}"}
            """;
    }

    [Fact]
    public void The_manifest_lists_every_container_from_the_api()
    {
        RespondWithSections(containers: Items(
            ContainerJson(ContainerId, "MSKU1234567", "Loaded", "40HC"),
            ContainerJson(SecondContainerId, "TGHU7654321")));

        var page = RenderDetailsPage();

        var rows = page.FindAll(".manifest-row");

        Assert.Equal(2, rows.Count);
        Assert.Contains("MSKU1234567", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("40HC", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Loaded", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Not assigned", rows[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_manifest_shows_how_many_containers_are_loaded()
    {
        RespondWithSections(containers: Items(
            ContainerJson(ContainerId, "MSKU1234567", "Loaded"),
            ContainerJson(SecondContainerId, "TGHU7654321")));

        var page = RenderDetailsPage();

        Assert.Equal("1 of 2 loaded", page.Find(".manifest-progress").TextContent.Trim());
    }

    [Fact]
    public void An_empty_manifest_invites_the_first_container()
    {
        RespondWithSections();

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".manifest-row"));
        Assert.Contains("No containers yet. Add the first one in the row below.", page.Markup, StringComparison.Ordinal);
        Assert.Single(page.FindAll(".manifest-add-row"));
    }

    [Fact]
    public void The_add_container_form_is_hidden_without_the_cargo_manage_permission()
    {
        RespondWithSections();

        var page = RenderDetailsPage(CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Empty(page.FindAll(".container-submit"));
    }

    [Fact]
    public void A_completed_booking_cannot_receive_containers()
    {
        RespondWithSections(details: Details(status: "Completed", nextStatus: null));

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".container-submit"));
        Assert.Contains("its manifest can no longer change", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_a_container_posts_it_and_reloads_the_manifest()
    {
        var posted = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posted++;

                return Json(
                    HttpStatusCode.Created,
                    """{"id":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","status":"Registered"}""");
            }

            return SectionResponse(
                request,
                posted == 0 ? EmptyItems : Items(ContainerJson(ContainerId, "MSKU1234567")),
                EmptyItems,
                Details());
        };

        var page = RenderDetailsPage();
        page.Find("#container-number").Change("MSKU1234567");
        page.Find("#container-type").Change("40HC");
        page.Find(".container-submit").Click();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".manifest-row")));
        Assert.Equal(1, posted);
    }

    [Fact]
    public void A_duplicate_container_number_is_reported_on_the_field()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Conflict,
                """
                {"title":"A container with this number already exists.","status":409,"errors":{"containerNumber":["Already registered."]}}
                """)
            : SectionResponse(request, EmptyItems, EmptyItems, Details());

        var page = RenderDetailsPage();
        page.Find("#container-number").Change("MSKU1234567");
        page.Find("#container-type").Change("40HC");
        page.Find(".container-submit").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("Already registered.", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll(".manifest-row"));
    }

    [Fact]
    public void A_manifest_that_fails_to_load_can_be_retried()
    {
        var attempts = 0;
        respond = request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/containers", StringComparison.Ordinal))
            {
                return SectionResponse(request, EmptyItems, EmptyItems, Details());
            }

            attempts++;

            return attempts == 1
                ? Json(HttpStatusCode.ServiceUnavailable, "{}")
                : Json(HttpStatusCode.OK, Items(ContainerJson(ContainerId, "MSKU1234567")));
        };

        var page = RenderDetailsPage();

        page.WaitForAssertion(() =>
            Assert.Contains("Unable to load the container manifest", page.Markup, StringComparison.Ordinal));
        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".manifest-row")));
    }

    [Fact]
    public void Operations_are_listed_with_the_state_derived_by_the_api()
    {
        RespondWithSections(operations: Items(
            OperationJson(OperationId, "Load", "InProgress", StartedStamp),
            OperationJson(SecondOperationId, "Unload", "Planned")));

        var page = RenderDetailsPage();

        var states = page.FindAll(".operation-state").Select(cell => cell.TextContent.Trim());

        Assert.Equal(["InProgress", "Planned"], states);
        Assert.Contains("Not finished", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void No_operations_shows_the_empty_state()
    {
        RespondWithSections();

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".operation-row"));
        Assert.Contains("No operations planned", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_is_offered_for_a_planned_operation_and_complete_for_one_in_progress()
    {
        RespondWithSections(operations: Items(
            OperationJson(OperationId, "Load", "Planned"),
            OperationJson(SecondOperationId, "Unload", "InProgress", StartedStamp)));

        var page = RenderDetailsPage();

        Assert.Single(page.FindAll(".operation-start"));
        Assert.Single(page.FindAll(".operation-complete"));
    }

    [Fact]
    public void A_completed_operation_offers_no_transition()
    {
        RespondWithSections(operations: Items(
            OperationJson(OperationId, "Load", "Completed", StartedStamp, FinishedStamp)));

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".operation-start"));
        Assert.Empty(page.FindAll(".operation-complete"));
        Assert.Contains("Settled", page.Find(".operation-actions").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_transition_actions_are_hidden_without_the_cargo_manage_permission()
    {
        RespondWithSections(operations: Items(OperationJson(OperationId, "Load", "Planned")));

        var page = RenderDetailsPage(CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Single(page.FindAll(".operation-row"));
        Assert.Empty(page.FindAll(".operation-start"));
        Assert.Empty(page.FindAll(".operation-submit"));
    }

    [Fact]
    public void Planning_an_operation_posts_the_type_and_the_time_in_utc()
    {
        string? body = null;
        respond = async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                body = await request.Content!.ReadAsStringAsync(CancellationToken.None);

                return await Json(
                    HttpStatusCode.Created,
                    """{"id":"80000000-0000-0000-0000-000000000007","operationType":"Unload","operationState":"Planned"}""");
            }

            return await SectionResponse(request, EmptyItems, EmptyItems, Details());
        };

        var page = RenderDetailsPage();
        page.Find(".operations-plan-toggle").Click();
        page.Find("#operation-type").Change("Unload");
        page.Find("#operation-planned").Change("2026-08-14T10:15:00");
        page.Find(".operation-submit").Click();

        page.WaitForAssertion(() => Assert.NotNull(body));
        Assert.Contains("\"operationType\":\"Unload\"", body, StringComparison.Ordinal);
        Assert.Contains("2026-08-14T10:15:00Z", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Starting_an_operation_asks_for_confirmation_before_posting()
    {
        var posted = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posted++;

                return Json(HttpStatusCode.OK, Transition("InProgress", StartedStamp, "null"));
            }

            return SectionResponse(
                request,
                EmptyItems,
                Items(OperationJson(OperationId, "Load", "Planned")),
                Details());
        };

        var page = RenderDetailsPage();
        page.Find(".operation-start").Click();

        Assert.Single(page.FindAll(".operation-modal"));
        Assert.Contains("Start the load operation", page.Find(".operation-confirm-question").TextContent, StringComparison.Ordinal);
        Assert.Equal(0, posted);
    }

    [Fact]
    public void Dismissing_the_confirmation_posts_nothing()
    {
        var posted = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posted++;

                return Json(HttpStatusCode.OK, Transition("InProgress", StartedStamp, "null"));
            }

            return SectionResponse(
                request,
                EmptyItems,
                Items(OperationJson(OperationId, "Load", "Planned")),
                Details());
        };

        var page = RenderDetailsPage();
        page.Find(".operation-start").Click();
        page.Find(".operation-confirm-dismiss").Click();

        Assert.Empty(page.FindAll(".operation-modal"));
        Assert.Equal(0, posted);
    }

    [Fact]
    public void Confirming_a_completion_posts_it_and_refreshes_the_manifest()
    {
        var posted = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posted++;

                return Json(HttpStatusCode.OK, Transition("Completed", StartedStamp, FinishedStamp));
            }

            return SectionResponse(
                request,
                posted == 0
                    ? Items(ContainerJson(ContainerId, "MSKU1234567"))
                    : Items(ContainerJson(ContainerId, "MSKU1234567", "Loaded")),
                posted == 0
                    ? Items(OperationJson(OperationId, "Load", "InProgress", StartedStamp))
                    : Items(OperationJson(OperationId, "Load", "Completed", StartedStamp, FinishedStamp)),
                Details());
        };

        var page = RenderDetailsPage();
        page.Find(".operation-complete").Click();
        page.Find(".operation-confirm").Click();

        page.WaitForAssertion(() =>
            Assert.Equal("1 of 1 loaded", page.Find(".manifest-progress").TextContent.Trim()));
        Assert.Equal(1, posted);
        Assert.Empty(page.FindAll(".operation-modal"));
    }

    [Fact]
    public void A_rejected_transition_is_reported_and_the_operations_are_reloaded()
    {
        var posted = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posted++;

                return Json(
                    HttpStatusCode.Conflict,
                    """
                    {"title":"Cargo operation transition is not allowed.","status":409,"detail":"It is already in progress."}
                    """);
            }

            return SectionResponse(
                request,
                EmptyItems,
                posted == 0
                    ? Items(OperationJson(OperationId, "Load", "Planned"))
                    : Items(OperationJson(OperationId, "Load", "InProgress", StartedStamp)),
                Details());
        };

        var page = RenderDetailsPage();
        page.Find(".operation-start").Click();
        page.Find(".operation-confirm").Click();

        page.WaitForAssertion(() =>
            Assert.Contains(
                "Cargo operation transition is not allowed.",
                page.Find(".booking-form-error").TextContent,
                StringComparison.Ordinal));
        page.Find(".operation-confirm-dismiss").Click();
        Assert.Single(page.FindAll(".operation-complete"));
    }

    [Fact]
    public void An_empty_manifest_shows_the_empty_state_without_the_manage_permission()
    {
        RespondWithSections();

        var page = RenderDetailsPage(CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Empty(page.FindAll(".manifest-add-row"));
        Assert.Contains("No containers on the manifest", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_manifest_bar_reflects_the_loaded_share()
    {
        RespondWithSections(containers: Items(
            ContainerJson(ContainerId, "MSKU1234567", "Loaded"),
            ContainerJson(SecondContainerId, "TGHU7654321")));

        var page = RenderDetailsPage();

        var bar = page.Find(".manifest-bar");

        Assert.Contains("width:50%", bar.InnerHtml, StringComparison.Ordinal);
        Assert.Equal("1 of 2 containers loaded", bar.GetAttribute("aria-label"));
    }

    [Fact]
    public void The_summary_counts_the_containers_and_operations()
    {
        RespondWithSections(
            containers: Items(ContainerJson(ContainerId, "MSKU1234567", "Loaded")),
            operations: Items(OperationJson(OperationId, "Load", "Planned")));

        var page = RenderDetailsPage();

        var kpi = page.Find(".kpi-containers");

        Assert.Contains("Containers", kpi.TextContent, StringComparison.Ordinal);
        Assert.Contains("1 loaded, 1 operations", kpi.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plan_form_stays_collapsed_until_it_is_opened()
    {
        RespondWithSections();

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll("#operation-type"));

        page.Find(".operations-plan-toggle").Click();

        Assert.Single(page.FindAll("#operation-type"));

        page.Find(".operations-plan-toggle").Click();

        Assert.Empty(page.FindAll("#operation-type"));
    }

    [Fact]
    public void The_operations_card_names_how_many_containers_a_completion_settles()
    {
        RespondWithSections(containers: Items(
            ContainerJson(ContainerId, "MSKU1234567"),
            ContainerJson(SecondContainerId, "TGHU7654321")));

        var page = RenderDetailsPage();

        Assert.Contains(
            "settles all 2 containers on this manifest",
            page.Find(".operations-effect").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Completion_is_blocked_while_the_manifest_is_empty()
    {
        RespondWithSections(operations: Items(
            OperationJson(OperationId, "Load", "InProgress", StartedStamp)));

        var page = RenderDetailsPage();

        Assert.True(page.Find(".operation-complete").HasAttribute("disabled"));
        Assert.Contains(
            "Add at least one container to the manifest before an operation can be completed.",
            page.Find(".operations-effect").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Completion_is_offered_once_the_manifest_has_a_container()
    {
        RespondWithSections(
            containers: Items(ContainerJson(ContainerId, "MSKU1234567")),
            operations: Items(OperationJson(OperationId, "Load", "InProgress", StartedStamp)));

        var page = RenderDetailsPage();

        Assert.False(page.Find(".operation-complete").HasAttribute("disabled"));
    }

    private void RespondWithSections(
        string? containers = null,
        string? operations = null,
        string? details = null)
    {
        respond = request => SectionResponse(
            request,
            containers ?? EmptyItems,
            operations ?? EmptyItems,
            details ?? Details());
    }

    private static Task<HttpResponseMessage> SectionResponse(
        HttpRequestMessage request,
        string containers,
        string operations,
        string details)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/containers", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, containers);
        }

        if (path.EndsWith("/operations", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, operations);
        }

        return Json(HttpStatusCode.OK, details);
    }

    private static string Items(params string[] rows)
    {
        return $$"""{"items":[{{string.Join(',', rows)}}]}""";
    }

    private static string ContainerJson(
        string containerId,
        string containerNumber,
        string status = "Registered",
        string containerType = "20GP")
    {
        return $$"""
            {"containerId":"{{containerId}}","containerNumber":"{{containerNumber}}","containerType":"{{containerType}}","status":"{{status}}","cargoBookingId":"{{BookingId}}","vesselId":null}
            """;
    }

    private static string OperationJson(
        string operationId,
        string operationType,
        string operationState,
        string startedAtUtc = "null",
        string finishedAtUtc = "null")
    {
        return $$"""
            {"operationId":"{{operationId}}","operationType":"{{operationType}}","operationState":"{{operationState}}","plannedAtUtc":"2026-08-12T06:00:00Z","startedAtUtc":{{startedAtUtc}},"finishedAtUtc":{{finishedAtUtc}},"responsibleUserId":"{{ResponsibleUserId}}"}
            """;
    }

    private static string Transition(string operationState, string startedAtUtc, string finishedAtUtc)
    {
        return $$"""
            {"operationId":"{{OperationId}}","operationState":"{{operationState}}","startedAtUtc":{{startedAtUtc}},"finishedAtUtc":{{finishedAtUtc}}}
            """;
    }

    private int DetailsRequestCount()
    {
        return requestedUris.Count(uri =>
            uri.AbsolutePath.Equals($"/api/cargo/bookings/{BookingId}", StringComparison.Ordinal));
    }

    private static string Details(
        string bookingNumber = "BK-2026-0145",
        string description = "Steel coils",
        string cargoType = "General Cargo",
        string weightKg = "5000.000",
        int packageCount = 10,
        bool isHazardous = false,
        string status = "Registered",
        string? nextStatus = "Planned",
        string? safetyInfo = null,
        string[]? trackingEvents = null)
    {
        return $$"""
            {"cargoBookingId":"{{BookingId}}","bookingNumber":"{{bookingNumber}}","voyageId":"{{VoyageId}}","description":"{{description}}","cargoType":"{{cargoType}}","weightKg":{{weightKg}},"packageCount":{{packageCount}},"isHazardous":{{(isHazardous ? "true" : "false")}},"status":"{{status}}","nextStatus":{{(nextStatus is null ? "null" : $"\"{nextStatus}\"")}},"createdUtc":"2026-08-11T09:42:00Z","safetyInfo":{{safetyInfo ?? "null"}},"trackingEvents":[{{string.Join(',', trackingEvents ?? [])}}]}
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
