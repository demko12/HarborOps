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
        {"cargoSafetyInfoId":"60000000-0000-0000-0000-000000000006","imdgClass":"5.1","unNumber":"UN1942","storageConditions":"Keep below 25C","handlingNotes":"No shock loading","reviewStatus":"Approved","submittedByUserId":"50000000-0000-0000-0000-000000000005","submittedAtUtc":"2026-09-01T10:00:00Z","reviewedByUserId":"50000000-0000-0000-0000-000000000005","reviewedAtUtc":"2026-09-01T14:30:00Z","reviewNotes":null}
        """;

    private const string DraftSafetyInfo = """
        {"cargoSafetyInfoId":"60000000-0000-0000-0000-000000000006","imdgClass":"5.1","unNumber":"UN1942","storageConditions":"Keep below 25C","handlingNotes":null,"reviewStatus":"Draft","submittedByUserId":null,"submittedAtUtc":null,"reviewedByUserId":null,"reviewedAtUtc":null,"reviewNotes":null}
        """;

    private const string SubmittedSafetyInfo = """
        {"cargoSafetyInfoId":"60000000-0000-0000-0000-000000000006","imdgClass":"5.1","unNumber":"UN1942","storageConditions":"Keep below 25C","handlingNotes":null,"reviewStatus":"Submitted","submittedByUserId":"50000000-0000-0000-0000-000000000005","submittedAtUtc":"2026-09-01T10:00:00Z","reviewedByUserId":null,"reviewedAtUtc":null,"reviewNotes":null}
        """;

    private const string RejectedSafetyInfo = """
        {"cargoSafetyInfoId":"60000000-0000-0000-0000-000000000006","imdgClass":"5.1","unNumber":"UN1942","storageConditions":"Keep below 25C","handlingNotes":null,"reviewStatus":"Rejected","submittedByUserId":"50000000-0000-0000-0000-000000000005","submittedAtUtc":"2026-09-01T10:00:00Z","reviewedByUserId":"50000000-0000-0000-0000-000000000005","reviewedAtUtc":"2026-09-01T14:30:00Z","reviewNotes":"Storage conditions are unclear."}
        """;

    private const string ReviewTimeline = """
        [{"cargoSafetyReviewId":"90000000-0000-0000-0000-000000000009","fromStatus":"Draft","toStatus":"Submitted","actorUserId":"50000000-0000-0000-0000-000000000005","occurredUtc":"2026-09-01T10:00:00Z","notes":null},{"cargoSafetyReviewId":"90000000-0000-0000-0000-000000000019","fromStatus":"Submitted","toStatus":"Rejected","actorUserId":"50000000-0000-0000-0000-000000000005","occurredUtc":"2026-09-01T14:30:00Z","notes":"Storage conditions are unclear."}]
        """;

    private const string SafetyNotFound = """
        {"title":"Cargo safety information not found.","status":404}
        """;

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = RespondWith(Details());

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

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
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: DeclaredSafetyInfo),
            DeclaredSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Contains("5.1", page.Markup, StringComparison.Ordinal);
        Assert.Contains("UN1942", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Keep below 25C", page.Markup, StringComparison.Ordinal);
        Assert.Contains("No shock loading", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_review_status_of_a_declaration_is_shown()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: SubmittedSafetyInfo),
            SubmittedSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Contains(
            "Awaiting review",
            page.Find(".safety-status").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_rejected_declaration_shows_the_reviewer_notes()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: RejectedSafetyInfo),
            RejectedSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Contains("Rejected", page.Find(".safety-status").TextContent, StringComparison.Ordinal);
        Assert.Contains(
            "Storage conditions are unclear.",
            page.Find(".safety-review-notes").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_review_timeline_is_listed_in_the_order_the_api_returned_it()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: RejectedSafetyInfo),
            RejectedSafetyInfo,
            ReviewTimeline);

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".safety-timeline-entry"));
        Assert.Contains("Review history (2)", page.Find(".safety-history-open").TextContent, StringComparison.Ordinal);

        page.Find(".safety-history-open").Click();

        var entries = page.FindAll(".safety-timeline-entry");

        Assert.Equal(2, entries.Count);
        Assert.Contains("Awaiting review", entries[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Rejected", entries[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Storage conditions are unclear.", entries[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_review_history_popup_shows_the_full_actor_id_and_closes()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: RejectedSafetyInfo),
            RejectedSafetyInfo,
            ReviewTimeline);

        var page = RenderDetailsPage();
        page.Find(".safety-history-open").Click();

        var actor = page.FindAll(".safety-timeline-actor")[0];

        Assert.Contains("User ID", actor.TextContent, StringComparison.Ordinal);
        Assert.Contains(
            "50000000-0000-0000-0000-000000000005",
            actor.TextContent,
            StringComparison.Ordinal);

        page.Find(".operation-confirm-dismiss").Click();

        Assert.Empty(page.FindAll(".safety-timeline-entry"));
    }

    [Fact]
    public void The_submitted_and_reviewed_rows_show_the_full_actor_id()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: RejectedSafetyInfo),
            RejectedSafetyInfo);

        var page = RenderDetailsPage();

        var actors = page.FindAll(".safety-audit-actor");

        Assert.Equal(2, actors.Count);
        Assert.All(
            actors,
            actor => Assert.Equal("50000000-0000-0000-0000-000000000005", actor.TextContent.Trim()));
    }

    [Fact]
    public void A_declaration_without_history_offers_no_history_button()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: DraftSafetyInfo),
            DraftSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".safety-history-open"));
    }

    [Fact]
    public void A_draft_declaration_shows_no_review_timeline()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: DraftSafetyInfo),
            DraftSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Contains("Draft", page.Find(".safety-status").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".safety-timeline-entry"));
    }

    [Fact]
    public void Hazardous_cargo_without_a_declaration_says_so()
    {
        respond = RespondWith(Details(isHazardous: true));

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
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: DeclaredSafetyInfo),
            DeclaredSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".safety-submit"));
    }

    [Fact]
    public void A_draft_declaration_can_be_edited_from_its_current_values()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: DraftSafetyInfo),
            DraftSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll("#safety-imdg-class"));

        page.Find(".safety-edit").Click();

        Assert.Equal("5.1", page.Find("#safety-imdg-class").GetAttribute("value"));
        Assert.Equal("UN1942", page.Find("#safety-un-number").GetAttribute("value"));
        Assert.Equal("Keep below 25C", page.Find("#safety-storage").GetAttribute("value"));
        Assert.Contains("Save changes", page.Find(".safety-submit").TextContent, StringComparison.Ordinal);
        Assert.Contains(
            "safety-facts-editing",
            page.Find("#safety-imdg-class").Closest(".booking-facts")!.GetAttribute("class"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_imdg_class_field_suggests_the_standard_divisions_without_locking_them()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: DraftSafetyInfo),
            DraftSafetyInfo);

        var page = RenderDetailsPage();

        page.Find(".safety-edit").Click();

        page.Find("#safety-imdg-class").Input(string.Empty);
        page.Find(".imdg-picker").FocusIn();

        var codes = page
            .FindAll(".imdg-picker-code")
            .Select(option => option.TextContent)
            .ToList();

        Assert.Contains("1.4", codes);
        Assert.Contains("6.2", codes);
        Assert.Contains("9", codes);
        Assert.Equal(20, codes.Count);

        Assert.Equal("input", page.Find("#safety-imdg-class").TagName, ignoreCase: true);
    }

    [Fact]
    public void Typing_narrows_the_imdg_list_and_free_text_is_kept()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: DraftSafetyInfo),
            DraftSafetyInfo);

        var page = RenderDetailsPage();

        page.Find(".safety-edit").Click();
        page.Find(".imdg-picker").FocusIn();
        page.Find("#safety-imdg-class").Input("1.");

        var codes = page
            .FindAll(".imdg-picker-code")
            .Select(option => option.TextContent)
            .ToList();

        Assert.Equal(6, codes.Count);
        Assert.All(codes, code => Assert.StartsWith("1.", code, StringComparison.Ordinal));

        page.Find("#safety-imdg-class").Input("1.4S");

        Assert.Empty(page.FindAll(".imdg-picker-code"));
        Assert.Equal("1.4S", page.Find("#safety-imdg-class").GetAttribute("value"));
    }

    [Fact]
    public void Picking_an_imdg_class_fills_the_field_and_closes_the_list()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: DraftSafetyInfo),
            DraftSafetyInfo);

        var page = RenderDetailsPage();

        page.Find(".safety-edit").Click();
        page.Find(".imdg-picker").FocusIn();
        page.Find("#safety-imdg-class").Input("6");
        page.FindAll(".imdg-picker-option")[1].MouseDown();

        Assert.Equal("6.2", page.Find("#safety-imdg-class").GetAttribute("value"));
        Assert.Empty(page.FindAll(".imdg-picker-dropdown"));
    }

    [Fact]
    public void The_submitted_and_reviewed_rows_stay_read_only_while_editing()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: RejectedSafetyInfo),
            RejectedSafetyInfo);

        var page = RenderDetailsPage();
        page.Find(".safety-edit").Click();

        var audit = page.Find(".safety-facts-audit");

        Assert.Contains("Submitted", audit.TextContent, StringComparison.Ordinal);
        Assert.Contains("Reviewed", audit.TextContent, StringComparison.Ordinal);
        Assert.Empty(audit.QuerySelectorAll("input, textarea"));
    }

    [Fact]
    public void Cancelling_an_edit_closes_the_form_and_keeps_the_stored_values()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: DraftSafetyInfo),
            DraftSafetyInfo);

        var page = RenderDetailsPage();
        page.Find(".safety-edit").Click();
        page.Find("#safety-un-number").Change("UN1950");
        page.Find(".safety-edit-cancel").Click();

        Assert.Empty(page.FindAll("#safety-un-number"));
        Assert.Contains("UN1942", page.Markup, StringComparison.Ordinal);

        page.Find(".safety-edit").Click();

        Assert.Equal("UN1942", page.Find("#safety-un-number").GetAttribute("value"));
    }

    [Fact]
    public void A_rejected_declaration_can_be_corrected()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: RejectedSafetyInfo),
            RejectedSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Single(page.FindAll(".safety-edit"));
        Assert.Empty(page.FindAll(".safety-locked"));
    }

    [Fact]
    public void A_submitted_declaration_is_locked_while_it_awaits_review()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: SubmittedSafetyInfo),
            SubmittedSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".safety-submit"));
        Assert.Contains(
            "awaiting review and cannot be edited",
            page.Find(".safety-locked").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_approved_declaration_is_locked_for_good()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: DeclaredSafetyInfo),
            DeclaredSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Contains(
            "approved and can no longer be edited",
            page.Find(".safety-locked").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_reader_cannot_edit_a_draft_declaration()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: DraftSafetyInfo),
            DraftSafetyInfo);

        var page = RenderDetailsPage(CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Empty(page.FindAll(".safety-submit"));
        Assert.Empty(page.FindAll(".safety-locked"));
        Assert.Contains("UN1942", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Editing_a_draft_sends_the_changed_values_and_reloads_them()
    {
        var sentBodies = new List<string>();
        respond = async request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                sentBodies.Add(await request.Content!.ReadAsStringAsync(CancellationToken.None));

                return await Json(HttpStatusCode.OK, DraftSafetyInfo);
            }

            if (IsSafetyRoute(request))
            {
                return await SafetyResponse(DraftSafetyInfo);
            }

            return await Json(
                HttpStatusCode.OK,
                Details(isHazardous: true, safetyInfo: DraftSafetyInfo));
        };

        var page = RenderDetailsPage();
        page.Find(".safety-edit").Click();
        page.Find("#safety-un-number").Change("UN1950");
        page.Find(".safety-submit").Click();

        page.WaitForAssertion(() => Assert.Single(sentBodies));

        Assert.Contains("\"unNumber\":\"UN1950\"", sentBodies[0], StringComparison.Ordinal);
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("#safety-un-number")));
        Assert.Contains("UN1942", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_draft_declaration_can_be_submitted_for_review()
    {
        var posted = new List<string>();
        respond = request =>
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/submit", StringComparison.Ordinal))
            {
                posted.Add(request.RequestUri.AbsolutePath);

                return Json(HttpStatusCode.OK, SubmittedSafetyInfo);
            }

            if (IsSafetyRoute(request))
            {
                return SafetyResponse(posted.Count == 0 ? DraftSafetyInfo : SubmittedSafetyInfo);
            }

            return Json(
                HttpStatusCode.OK,
                Details(
                    isHazardous: true,
                    safetyInfo: posted.Count == 0 ? DraftSafetyInfo : SubmittedSafetyInfo));
        };

        var page = RenderDetailsPage();
        page.Find(".safety-action-submit").Click();
        page.Find(".safety-confirm").Click();

        page.WaitForAssertion(() => Assert.Single(posted));

        Assert.Equal($"/api/cargo/bookings/{BookingId}/safety-information/submit", posted[0]);
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".operation-modal")));
        page.WaitForAssertion(() =>
            Assert.Contains("Awaiting review", page.Find(".safety-status").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void A_submitted_declaration_offers_approve_and_reject()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: SubmittedSafetyInfo),
            SubmittedSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Single(page.FindAll(".safety-action-approve"));
        Assert.Single(page.FindAll(".safety-action-reject"));
        Assert.Empty(page.FindAll(".safety-action-submit"));
    }

    [Fact]
    public void Approving_a_declaration_sends_the_decision()
    {
        var bodies = new List<string>();
        respond = async request =>
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/review", StringComparison.Ordinal))
            {
                bodies.Add(await request.Content!.ReadAsStringAsync(CancellationToken.None));

                return await Json(HttpStatusCode.OK, DeclaredSafetyInfo);
            }

            if (IsSafetyRoute(request))
            {
                return await SafetyResponse(bodies.Count == 0 ? SubmittedSafetyInfo : DeclaredSafetyInfo);
            }

            return await Json(
                HttpStatusCode.OK,
                Details(
                    isHazardous: true,
                    safetyInfo: bodies.Count == 0 ? SubmittedSafetyInfo : DeclaredSafetyInfo));
        };

        var page = RenderDetailsPage();
        page.Find(".safety-action-approve").Click();
        page.Find(".safety-confirm").Click();

        page.WaitForAssertion(() => Assert.Single(bodies));

        Assert.Contains("\"status\":\"Approved\"", bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Rejecting_a_declaration_sends_the_reviewer_notes()
    {
        var bodies = new List<string>();
        respond = async request =>
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/review", StringComparison.Ordinal))
            {
                bodies.Add(await request.Content!.ReadAsStringAsync(CancellationToken.None));

                return await Json(HttpStatusCode.OK, RejectedSafetyInfo);
            }

            if (IsSafetyRoute(request))
            {
                return await SafetyResponse(bodies.Count == 0 ? SubmittedSafetyInfo : RejectedSafetyInfo);
            }

            return await Json(
                HttpStatusCode.OK,
                Details(
                    isHazardous: true,
                    safetyInfo: bodies.Count == 0 ? SubmittedSafetyInfo : RejectedSafetyInfo));
        };

        var page = RenderDetailsPage();
        page.Find(".safety-action-reject").Click();
        page.Find("#safety-review-notes").Change("Storage conditions are unclear.");
        page.Find(".safety-confirm").Click();

        page.WaitForAssertion(() => Assert.Single(bodies));

        Assert.Contains("\"status\":\"Rejected\"", bodies[0], StringComparison.Ordinal);
        Assert.Contains("Storage conditions are unclear.", bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_rejection_without_notes_is_not_sent_to_the_api()
    {
        var reviews = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/review", StringComparison.Ordinal))
            {
                reviews++;
            }

            if (IsSafetyRoute(request))
            {
                return SafetyResponse(SubmittedSafetyInfo);
            }

            return Json(HttpStatusCode.OK, Details(isHazardous: true, safetyInfo: SubmittedSafetyInfo));
        };

        var page = RenderDetailsPage();
        page.Find(".safety-action-reject").Click();
        page.Find(".safety-confirm").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("Review notes are required", page.Markup, StringComparison.Ordinal));
        Assert.Equal(0, reviews);
        Assert.Single(page.FindAll(".operation-modal"));
    }

    [Fact]
    public void A_conflict_while_reviewing_is_reported_and_the_panel_is_refreshed()
    {
        var attempted = false;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/review", StringComparison.Ordinal))
            {
                attempted = true;

                return Json(
                    HttpStatusCode.Conflict,
                    """
                    {"title":"Cargo safety information transition is not allowed.","status":409}
                    """);
            }

            if (IsSafetyRoute(request))
            {
                return SafetyResponse(attempted ? DeclaredSafetyInfo : SubmittedSafetyInfo);
            }

            return Json(
                HttpStatusCode.OK,
                Details(
                    isHazardous: true,
                    safetyInfo: attempted ? DeclaredSafetyInfo : SubmittedSafetyInfo));
        };

        var page = RenderDetailsPage();
        page.Find(".safety-action-approve").Click();
        page.Find(".safety-confirm").Click();

        page.WaitForAssertion(() =>
            Assert.Contains(
                "transition is not allowed",
                page.Find(".operation-modal .booking-form-error").TextContent,
                StringComparison.Ordinal));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".safety-action-approve")));
    }

    [Fact]
    public void The_confirmation_can_be_dismissed_without_acting()
    {
        var posts = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posts++;
            }

            if (IsSafetyRoute(request))
            {
                return SafetyResponse(SubmittedSafetyInfo);
            }

            return Json(HttpStatusCode.OK, Details(isHazardous: true, safetyInfo: SubmittedSafetyInfo));
        };

        var page = RenderDetailsPage();
        page.Find(".safety-action-approve").Click();

        Assert.Single(page.FindAll(".operation-modal"));

        page.Find(".operation-confirm-dismiss").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".operation-modal")));
        Assert.Equal(0, posts);
    }

    [Fact]
    public async Task The_safety_panel_reports_that_it_is_loading()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        respond = request => IsSafetyRoute(request)
            ? release.Task
            : Json(HttpStatusCode.OK, Details(isHazardous: true, safetyInfo: DraftSafetyInfo));

        var page = RenderDetailsPage();

        page.WaitForAssertion(() =>
            Assert.Contains("Loading safety information", page.Markup, StringComparison.Ordinal));

        release.SetResult(await SafetyResponse(DraftSafetyInfo));

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".safety-status")));
    }

    [Fact]
    public void An_api_failure_in_the_safety_panel_can_be_retried()
    {
        var attempts = 0;
        respond = request =>
        {
            if (!IsSafetyRoute(request))
            {
                return Json(HttpStatusCode.OK, Details(isHazardous: true, safetyInfo: DraftSafetyInfo));
            }

            attempts++;

            return attempts == 1
                ? Json(
                    HttpStatusCode.InternalServerError,
                    """
                    {"title":"Safety service is unavailable.","status":500}
                    """)
                : SafetyResponse(DraftSafetyInfo);
        };

        var page = RenderDetailsPage();

        page.WaitForAssertion(() =>
            Assert.Contains("Safety service is unavailable.", page.Markup, StringComparison.Ordinal));

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".safety-status")));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Disposing_the_page_cancels_the_pending_safety_request()
    {
        var started = new TaskCompletionSource();
        var observed = CancellationToken.None;
        respondWithToken = (request, token) =>
        {
            if (!IsSafetyRoute(request))
            {
                return Json(HttpStatusCode.OK, Details(isHazardous: true, safetyInfo: DraftSafetyInfo));
            }

            observed = token;
            started.TrySetResult();

            return new TaskCompletionSource<HttpResponseMessage>().Task;
        };

        RenderDetailsPage();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    [Fact]
    public void A_declaration_can_be_resubmitted_after_a_server_validation_error()
    {
        var attempts = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                attempts++;

                return attempts == 1
                    ? Json(
                        HttpStatusCode.BadRequest,
                        """
                        {"title":"One or more validation errors occurred.","status":400,"errors":{"unNumber":["UN number must be 4 digits."]}}
                        """)
                    : Json(HttpStatusCode.OK, DraftSafetyInfo);
            }

            if (IsSafetyRoute(request))
            {
                return SafetyResponse(DraftSafetyInfo);
            }

            return Json(HttpStatusCode.OK, Details(isHazardous: true, safetyInfo: DraftSafetyInfo));
        };

        var page = RenderDetailsPage();
        page.Find(".safety-edit").Click();
        page.Find(".safety-submit").Click();

        page.WaitForAssertion(() => Assert.Equal(1, attempts));
        page.WaitForAssertion(() =>
            Assert.Contains("UN number must be 4 digits.", page.Markup, StringComparison.Ordinal));

        page.Find("#safety-un-number").Change("UN1950");
        page.Find(".safety-submit").Click();

        page.WaitForAssertion(() => Assert.Equal(2, attempts));
    }

    [Fact]
    public void A_reader_is_offered_no_review_actions()
    {
        respond = RespondWith(
            Details(isHazardous: true, safetyInfo: SubmittedSafetyInfo),
            SubmittedSafetyInfo);

        var page = RenderDetailsPage(CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Empty(page.FindAll(".safety-actions"));
    }

    [Fact]
    public void A_conflict_while_saving_locks_the_form_against_the_reloaded_state()
    {
        var attempted = false;
        respond = request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                attempted = true;

                return Json(
                    HttpStatusCode.Conflict,
                    """
                    {"title":"Cargo safety information transition is not allowed.","status":409}
                    """);
            }

            if (IsSafetyRoute(request))
            {
                return SafetyResponse(attempted ? SubmittedSafetyInfo : DraftSafetyInfo);
            }

            return Json(
                HttpStatusCode.OK,
                Details(isHazardous: true, safetyInfo: attempted ? SubmittedSafetyInfo : DraftSafetyInfo));
        };

        var page = RenderDetailsPage();
        page.Find(".safety-edit").Click();
        page.Find(".safety-submit").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".safety-submit")));
        Assert.Contains(
            "awaiting review and cannot be edited",
            page.Find(".safety-locked").TextContent,
            StringComparison.Ordinal);
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
    public void Declaring_safety_information_puts_it_to_the_booking_safety_route()
    {
        var sentBodies = new List<string>();
        respond = async request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                sentBodies.Add(await request.Content!.ReadAsStringAsync(CancellationToken.None));

                return await Json(HttpStatusCode.OK, DeclaredSafetyInfo);
            }

            if (IsSafetyRoute(request))
            {
                return await SafetyResponse(sentBodies.Count == 0 ? null : DeclaredSafetyInfo);
            }

            return await Json(
                HttpStatusCode.OK,
                Details(isHazardous: true, safetyInfo: sentBodies.Count == 0 ? null : DeclaredSafetyInfo));
        };

        var page = RenderDetailsPage();
        FillSafetyForm(page);
        page.Find(".safety-submit").Click();

        page.WaitForAssertion(() => Assert.Single(sentBodies));

        var safetyUris = requestedUris
            .Where(uri => uri.AbsolutePath.EndsWith("safety-information", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(safetyUris);
        Assert.All(
            safetyUris,
            uri => Assert.Equal($"/api/cargo/bookings/{BookingId}/safety-information", uri.AbsolutePath));
        Assert.Contains("\"imdgClass\":\"5.1\"", sentBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"unNumber\":\"UN1942\"", sentBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"storageConditions\":\"Keep below 25C\"", sentBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_declaration_replaces_the_form_with_the_reloaded_safety_information()
    {
        var declared = false;
        respond = request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                declared = true;

                return Json(HttpStatusCode.OK, DeclaredSafetyInfo);
            }

            if (IsSafetyRoute(request))
            {
                return SafetyResponse(declared ? DeclaredSafetyInfo : null);
            }

            return Json(
                HttpStatusCode.OK,
                Details(isHazardous: true, safetyInfo: declared ? DeclaredSafetyInfo : null));
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
        var sent = 0;
        respond = request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                sent++;
            }

            return Json(HttpStatusCode.OK, Details(isHazardous: true));
        };

        var page = RenderDetailsPage();
        page.Find(".safety-submit").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("IMDG class is required.", page.Markup, StringComparison.Ordinal));
        Assert.Equal(0, sent);
    }

    [Fact]
    public void A_rejected_declaration_reports_the_error_on_its_field()
    {
        respond = request => request.Method == HttpMethod.Put
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
        respond = request => request.Method == HttpMethod.Put
            ? Json(
                HttpStatusCode.Conflict,
                """
                {"title":"Cargo safety information transition is not allowed.","status":409,"detail":"The declaration is already approved."}
                """)
            : Json(HttpStatusCode.OK, Details(isHazardous: true));

        var page = RenderDetailsPage();
        FillSafetyForm(page);
        page.Find(".safety-submit").Click();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".booking-form-error")));
        Assert.Contains(
            "Cargo safety information transition is not allowed.",
            page.Find(".booking-form-error").TextContent,
            StringComparison.Ordinal);
    }


    [Fact]
    public void Only_the_next_status_from_the_api_is_offered()
    {
        respond = _ => Json(HttpStatusCode.OK, Details(status: "Loaded", nextStatus: "InTransit"));

        var page = RenderDetailsPage();

        Assert.Contains("Record InTransit", page.Find(".tracking-submit").TextContent, StringComparison.Ordinal);

        // No tracking-status picker exists, so no other step in the vocabulary can be requested.
        Assert.Empty(page.FindAll(".booking-tracking-card select"));
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
        respond = RespondWith(
            Details(
                isHazardous: true,
                status: "Planned",
                nextStatus: "Loaded",
                safetyInfo: DeclaredSafetyInfo),
            DeclaredSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Contains("Record Loaded", page.Find(".tracking-submit").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unapproved_declaration_blocks_loading_from_the_page()
    {
        respond = RespondWith(
            Details(
                isHazardous: true,
                status: "Planned",
                nextStatus: "Loaded",
                safetyInfo: SubmittedSafetyInfo),
            SubmittedSafetyInfo);

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".tracking-submit"));
        Assert.Contains(
            "must be approved before this hazardous cargo can be loaded",
            page.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_declaration_blocks_loading_with_its_own_message()
    {
        respond = RespondWith(
            Details(isHazardous: true, status: "Planned", nextStatus: "Loaded"));

        var page = RenderDetailsPage();

        Assert.Empty(page.FindAll(".tracking-submit"));
        Assert.Contains(
            "Declare the safety information before this hazardous cargo can be loaded",
            page.Markup,
            StringComparison.Ordinal);
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
        return RenderDetailsPageAt(
            boardQuery is null
                ? $"/cargo/{BookingId}"
                : $"/cargo/{BookingId}?from={Uri.EscapeDataString(boardQuery)}",
            permissions);
    }

    private IRenderedComponent<DetailsPage> RenderDetailsPageAt(
        string address,
        params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler((request, token) =>
        {
            requestedUris.Add(request.RequestUri!);

            return respondWithToken is null ? respond(request) : respondWithToken(request, token);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CargoBookingsApiClient(httpClient));
        ConfigureAuthorization(permissions.Length == 0
            ? [CurrentUserAuthorizationState.CargoManagePermission]
            : permissions);

        // A query-string parameter can only reach the component through the address bar.
        Services.GetRequiredService<NavigationManager>().NavigateTo(address);

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
        Assert.Contains("No containers yet. Add the first one using the form below.", page.Markup, StringComparison.Ordinal);
        Assert.Single(page.FindAll(".manifest-add-form"));
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
        page.Find("#container-type").Change("45G1");
        page.Find("#container-pol").Change("NLRTM");
        page.Find("#container-pod").Change("USNYC");
        page.Find("#container-weight").Change("12500");
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
        page.Find("#container-type").Change("45G1");
        page.Find("#container-pol").Change("NLRTM");
        page.Find("#container-pod").Change("USNYC");
        page.Find("#container-weight").Change("12500");
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
        Assert.Contains("1 planned", page.Find(".booking-operations-card .booking-count").TextContent, StringComparison.Ordinal);
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

        Assert.Empty(page.FindAll(".manifest-add-form"));
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

    [Fact]
    public void The_manifest_shows_the_control_state_of_every_container()
    {
        respond = request => SectionResponse(
            request,
            Items(
                ContainerJson(ContainerId, "MSKU1234567"),
                ContainerJson(SecondContainerId, "MSKU7654321")),
            EmptyItems,
            Details(containerControls:
            [
                ContainerControlJson(ContainerId, "Released", releaseBlockedReason: null),
                ContainerControlJson(SecondContainerId, "Held", "MSKU7654321", "the container is held"),
            ]));

        var page = RenderDetailsPage();

        var states = page.FindAll(".manifest-control .control-status");

        Assert.Equal(2, states.Count);
        Assert.Contains("Released", states[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("On hold", states[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_container_without_a_control_record_is_shown_as_not_released()
    {
        respond = request => SectionResponse(
            request,
            Items(ContainerJson(ContainerId, "MSKU1234567")),
            EmptyItems,
            Details());

        var page = RenderDetailsPage();

        Assert.Contains(
            "Awaiting release",
            page.Find(".manifest-control .control-status").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_a_container_control_loads_it_and_closing_it_hides_the_panel()
    {
        respond = request =>
            request.RequestUri!.AbsolutePath.EndsWith("/control", StringComparison.Ordinal)
                ? Json(
                    HttpStatusCode.OK,
                    $$"""{"control":{{ContainerControlJson(ContainerId, "Pending")}},"history":[]}""")
                : SectionResponse(
                    request,
                    Items(ContainerJson(ContainerId, "MSKU1234567")),
                    EmptyItems,
                    Details(containerControls: [ContainerControlJson(ContainerId, "Pending")]));

        var page = RenderDetailsPage();

        page.Find(".manifest-control-open").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".container-control-panel")));
        Assert.Contains(
            $"/api/cargo/containers/{ContainerId}/control",
            requestedUris.Select(uri => uri.AbsolutePath));

        page.Find(".manifest-control-open").Click();

        Assert.Empty(page.FindAll(".container-control-panel"));
    }

    [Fact]
    public void A_seal_conflict_keeps_the_panel_and_its_message_on_the_page()
    {
        respond = request =>
            request.Method == HttpMethod.Post
                ? Json(
                    HttpStatusCode.Conflict,
                    """{"title":"Seal number is already in use.","status":409,"detail":"Seal number 'ML458821' is already active on another container."}""")
                : request.RequestUri!.AbsolutePath.EndsWith("/control", StringComparison.Ordinal)
                    ? Json(
                        HttpStatusCode.OK,
                        $$"""{"control":{{ContainerControlJson(ContainerId, "Pending")}},"history":[]}""")
                    : SectionResponse(
                        request,
                        Items(ContainerJson(ContainerId, "MSKU1234567")),
                        EmptyItems,
                        Details(containerControls: [ContainerControlJson(ContainerId, "Pending")]));

        var page = RenderDetailsPage();
        page.Find(".manifest-control-open").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".control-seal-form")));

        var beforeSubmit = requestedUris.Count;

        page.Find("#control-seal-number").Change("ML458821");
        page.Find(".control-seal-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "already in use",
            page.Markup,
            StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll(".container-control-panel"));

        var refreshed = requestedUris
            .Skip(beforeSubmit)
            .Select(uri => uri.AbsolutePath)
            .ToList();

        Assert.DoesNotContain(
            refreshed,
            path => path.EndsWith("/containers", StringComparison.Ordinal));
    }

    [Fact]
    public void Searching_finds_a_container_on_a_later_page_while_the_booking_counts_stay_whole()
    {
        var manifest = NumberedContainers("MSKU", 20, status: "Loaded");
        RespondWithManifest(manifest);

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(15, page.FindAll(".manifest-row").Count));

        page.Find("#manifest-search").Input("0000019");

        page.WaitForAssertion(
            () => Assert.Equal("MSKU0000019", Assert.Single(page.FindAll(".manifest-number")).TextContent),
            TimeSpan.FromSeconds(3));
        Assert.Equal("1 of 20 containers match", page.Find(".manifest-results").TextContent.Trim());
        Assert.Equal("20 of 20 loaded", page.Find(".manifest-progress").TextContent.Trim());
        Assert.Contains("20", page.Find(".kpi-containers .booking-kpi-value").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".manifest-pagination"));
        Assert.Equal("0000019", ContainerQuery(LastContainerRequest(), "search"));
        Assert.Equal("1", ContainerQuery(LastContainerRequest(), "pageNumber"));
    }

    [Fact]
    public async Task Typing_a_search_sends_one_request_for_the_final_text()
    {
        RespondWithManifest(NumberedContainers("MSKU", 3));

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll(".manifest-row").Count));

        await page.InvokeAsync(() => page.Find("#manifest-search").Input("M"));
        await page.InvokeAsync(() => page.Find("#manifest-search").Input("MS"));
        await page.InvokeAsync(() => page.Find("#manifest-search").Input("MSKU0000002"));

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".manifest-row")),
            TimeSpan.FromSeconds(3));
        Assert.Equal(
            ["MSKU0000002"],
            ContainerRequests().Select(uri => ContainerQuery(uri, "search")).OfType<string>());
    }

    [Fact]
    public async Task An_older_search_response_never_replaces_a_newer_one()
    {
        var manifest = new List<ManifestContainer>
        {
            new("AAAU0000001"),
            new("BBBU0000001"),
        };
        var olderRequestSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var olderResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        respond = request =>
        {
            if (IsContainersRequest(request) && ContainerQuery(request.RequestUri!, "search") == "AAAU")
            {
                olderRequestSent.TrySetResult();

                return olderResponse.Task;
            }

            return ManifestResponse(request, manifest);
        };

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".manifest-row").Count));

        await page.InvokeAsync(() => page.Find("#manifest-search").Input("AAAU"));
        await olderRequestSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await page.InvokeAsync(() => page.Find("#manifest-search").Input("BBBU"));
        page.WaitForAssertion(
            () => Assert.Equal("BBBU0000001", Assert.Single(page.FindAll(".manifest-number")).TextContent),
            TimeSpan.FromSeconds(3));

        olderResponse.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ManifestPage(manifest.Take(1).ToList(), manifest.Count, 1, 0, 1), Encoding.UTF8, "application/json"),
        });
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        page.Render();

        Assert.Equal("BBBU0000001", Assert.Single(page.FindAll(".manifest-number")).TextContent);
        Assert.Equal("BBBU", page.FindAll(".manifest-active-filter").Single().TextContent.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]);
    }

    [Fact]
    public async Task Every_filter_is_normalized_combined_and_resets_to_page_one()
    {
        var manifest = NumberedContainers("MSKU", 20, status: "Loaded", containerType: "42R1");
        RespondWithManifest(manifest);

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(15, page.FindAll(".manifest-row").Count));
        await page.InvokeAsync(() => page.Find(".manifest-pagination button:last-child").Click());
        page.WaitForAssertion(() => Assert.Equal("2", ContainerQuery(LastContainerRequest(), "pageNumber")));

        page.Find("#manifest-status").Change("Loaded");
        page.Find("#manifest-type").Change("42R1");
        await page.InvokeAsync(() => page.Find("#manifest-pol").Input("nlrtm"));
        await page.InvokeAsync(() => page.Find("#manifest-pod").Input("usnyc"));

        page.WaitForAssertion(
            () =>
            {
                var request = LastContainerRequest();

                Assert.Equal("Loaded", ContainerQuery(request, "status"));
                Assert.Equal("42R1", ContainerQuery(request, "containerType"));
                Assert.Equal("NLRTM", ContainerQuery(request, "portOfLoadingCode"));
                Assert.Equal("USNYC", ContainerQuery(request, "portOfDischargeCode"));
                Assert.Equal("1", ContainerQuery(request, "pageNumber"));
            },
            TimeSpan.FromSeconds(3));
        Assert.Equal(
            ["StatusLoaded", "Type42R1", "POLNLRTM", "PODUSNYC"],
            page.FindAll(".manifest-active-filter").Select(chip => chip.TextContent.Replace(" ", string.Empty, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Paging_keeps_the_applied_filters_and_counts_only_the_matching_rows()
    {
        var manifest = NumberedContainers("MSKU", 17).Concat(NumberedContainers("TGHU", 10)).ToList();
        RespondWithManifest(manifest);

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Contains("of 27 containers", page.Find(".manifest-pagination").TextContent, StringComparison.Ordinal));

        page.Find("#manifest-search").Input("msku");
        page.WaitForAssertion(
            () => Assert.Contains("Showing 1–15 of 17 matching containers", page.Find(".manifest-pagination").TextContent, StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));
        await page.InvokeAsync(() => page.Find(".manifest-pagination button:last-child").Click());

        page.WaitForAssertion(() =>
            Assert.Equal(["MSKU0000016", "MSKU0000017"], page.FindAll(".manifest-number").Select(cell => cell.TextContent)));
        Assert.Equal("MSKU", ContainerQuery(LastContainerRequest(), "search"));
        Assert.Equal("2", ContainerQuery(LastContainerRequest(), "pageNumber"));
        Assert.Contains("Page 2 of 2", page.Find(".manifest-pagination").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reset_clears_every_filter_and_reloads_the_whole_manifest()
    {
        RespondWithManifest(NumberedContainers("MSKU", 3, status: "Loaded").Append(new ManifestContainer("TGHU0000001")).ToList());

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".manifest-row").Count));
        Assert.True(page.Find(".manifest-reset").HasAttribute("disabled"));

        page.Find("#manifest-status").Change("Loaded");
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll(".manifest-row").Count));
        await page.InvokeAsync(() => page.Find(".manifest-reset").Click());

        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".manifest-row").Count));
        Assert.Empty(page.FindAll(".manifest-active-filter"));
        Assert.Equal(string.Empty, page.Find("#manifest-status").GetAttribute("value") ?? string.Empty);
        Assert.Null(ContainerQuery(LastContainerRequest(), "status"));
    }

    [Fact]
    public void No_matching_containers_is_distinguished_from_an_empty_manifest_and_add_stays_available()
    {
        RespondWithManifest(NumberedContainers("MSKU", 2));

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".manifest-row").Count));

        page.Find("#manifest-pol").Input("DEHAM");

        page.WaitForAssertion(
            () => Assert.Contains("No containers match these filters", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));
        Assert.Empty(page.FindAll(".manifest-table"));
        Assert.DoesNotContain("No containers yet", page.Markup, StringComparison.Ordinal);
        Assert.Single(page.FindAll(".manifest-add-form"));
        Assert.Single(page.FindAll(".manifest-import"));
        Assert.Equal("0 of 2 containers match", page.Find(".manifest-results").TextContent.Trim());
    }

    [Fact]
    public void An_empty_manifest_offers_no_filters()
    {
        RespondWithManifest([]);

        var page = RenderDetailsPage();

        page.WaitForAssertion(() =>
            Assert.Contains("No containers yet. Add the first one using the form below.", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll(".manifest-filters"));
        Assert.DoesNotContain("No containers match these filters", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reader_can_search_and_filter_without_being_offered_mutations()
    {
        RespondWithManifest(NumberedContainers("MSKU", 2).Append(new ManifestContainer("TGHU0000001", "Loaded")).ToList());

        var page = RenderDetailsPage(CurrentUserAuthorizationState.CargoReadPermission);
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll(".manifest-row").Count));

        page.Find("#manifest-status").Change("Loaded");

        page.WaitForAssertion(() =>
            Assert.Equal("TGHU0000001", Assert.Single(page.FindAll(".manifest-number")).TextContent));
        Assert.Empty(page.FindAll(".manifest-add-form"));
        Assert.Empty(page.FindAll(".manifest-import"));
    }

    [Fact]
    public void An_invalid_port_code_is_explained_without_calling_the_api()
    {
        RespondWithManifest(NumberedContainers("MSKU", 2));

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".manifest-row").Count));
        var before = ContainerRequests().Count;

        page.Find("#manifest-pol").Input("NL-R");

        page.WaitForAssertion(
            () => Assert.Equal("POL must be a five-character UN/LOCODE.", page.Find(".manifest-filter-error").TextContent.Trim()),
            TimeSpan.FromSeconds(3));
        Assert.Equal(before, ContainerRequests().Count);
        Assert.Equal(2, page.FindAll(".manifest-row").Count);
    }

    [Fact]
    public async Task A_port_code_is_applied_as_soon_as_it_is_complete_without_pressing_enter()
    {
        RespondWithManifest(NumberedContainers("MSKU", 2).Append(new ManifestContainer("TGHU0000001", PortOfLoadingCode: "DEHAM")).ToList());

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll(".manifest-row").Count));
        var before = ContainerRequests().Count;

        await page.InvokeAsync(() => page.Find("#manifest-pol").Input("de"));
        await page.InvokeAsync(() => page.Find("#manifest-pol").Input("deh"));
        page.WaitForAssertion(
            () => Assert.Contains(
                "Enter all 5 characters of the POL code",
                page.Find(".manifest-filter-hint").TextContent,
                StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));
        Assert.Equal(before, ContainerRequests().Count);
        Assert.Empty(page.FindAll(".manifest-filter-error"));
        Assert.Equal("e.g. NLRTM", page.Find("#manifest-pol").GetAttribute("placeholder"));

        await page.InvokeAsync(() => page.Find("#manifest-pol").Input("deham"));

        page.WaitForAssertion(
            () => Assert.Equal("TGHU0000001", Assert.Single(page.FindAll(".manifest-number")).TextContent),
            TimeSpan.FromSeconds(3));
        Assert.Empty(page.FindAll(".manifest-filter-hint"));
        Assert.Equal("DEHAM", ContainerQuery(LastContainerRequest(), "portOfLoadingCode"));
        Assert.Equal(before + 1, ContainerRequests().Count);
    }

    [Fact]
    public async Task Leaving_an_incomplete_port_code_explains_what_is_missing()
    {
        RespondWithManifest(NumberedContainers("MSKU", 2));

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".manifest-row").Count));
        var before = ContainerRequests().Count;

        await page.InvokeAsync(() => page.Find("#manifest-pod").Input("USN"));
        await page.InvokeAsync(() => page.Find("#manifest-pod").Blur());

        page.WaitForAssertion(() =>
            Assert.Equal("POD must be a five-character UN/LOCODE.", page.Find(".manifest-filter-error").TextContent.Trim()));
        Assert.Equal(before, ContainerRequests().Count);
    }

    [Fact]
    public async Task A_filter_rejected_by_the_api_is_reported_beside_the_filters_and_can_be_reset()
    {
        respond = request => IsContainersRequest(request) && ContainerQuery(request.RequestUri!, "status") is not null
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","status":400,"errors":{"status":["Status is not a known container status."]}}""")
            : ManifestResponse(request, NumberedContainers("MSKU", 2));

        var page = RenderDetailsPageAt($"/cargo/{BookingId}?manifestStatus=Lost");

        page.WaitForAssertion(() =>
            Assert.Equal("Status is not a known container status.", page.Find(".manifest-filter-error").TextContent.Trim()));
        Assert.Empty(page.FindAll(".api-error-state"));

        await page.InvokeAsync(() => page.Find(".manifest-reset").Click());

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".manifest-row").Count));
        Assert.Empty(page.FindAll(".manifest-filter-error"));
    }

    [Fact]
    public async Task A_failed_filtered_load_can_be_retried_with_the_same_filters()
    {
        var failNext = false;
        var manifest = NumberedContainers("MSKU", 3);
        respond = request =>
        {
            if (IsContainersRequest(request) && failNext)
            {
                failNext = false;

                return Json(HttpStatusCode.ServiceUnavailable, "{}");
            }

            return ManifestResponse(request, manifest);
        };

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll(".manifest-row").Count));

        failNext = true;
        page.Find("#manifest-search").Input("0000003");
        page.WaitForAssertion(
            () => Assert.Contains("Unable to load the container manifest", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));
        Assert.Single(page.FindAll(".manifest-filters"));
        await page.InvokeAsync(() => page.Find(".api-error-state button").Click());

        page.WaitForAssertion(() =>
            Assert.Equal("MSKU0000003", Assert.Single(page.FindAll(".manifest-number")).TextContent));
        Assert.Equal("0000003", ContainerQuery(LastContainerRequest(), "search"));
    }

    [Fact]
    public void Applied_filters_and_page_are_kept_in_the_address()
    {
        RespondWithManifest(NumberedContainers("MSKU", 20, status: "Loaded"));

        var page = RenderDetailsPageFrom("status=InTransit");
        page.WaitForAssertion(() => Assert.Equal(15, page.FindAll(".manifest-row").Count));

        page.Find("#manifest-status").Change("Loaded");

        page.WaitForAssertion(() =>
        {
            var address = new Uri(Services.GetRequiredService<NavigationManager>().Uri);

            Assert.Equal($"/cargo/{BookingId}", address.AbsolutePath);
            Assert.Equal("Loaded", ContainerQuery(address, "manifestStatus"));
            Assert.Equal("status=InTransit", ContainerQuery(address, "from"));
        });
        Assert.Contains("/cargo?status=InTransit", page.Find(".page-breadcrumb a").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    public void Filters_in_the_address_are_applied_to_the_first_request()
    {
        RespondWithManifest(NumberedContainers("MSKU", 20, containerType: "42R1"));

        var page = RenderDetailsPageAt(
            $"/cargo/{BookingId}?manifestSearch=msku&manifestType=42r1&manifestPol=NLRTM&manifestPage=2");

        page.WaitForAssertion(() => Assert.Equal(5, page.FindAll(".manifest-row").Count));
        var firstRequest = ContainerRequests()[0];
        Assert.Equal("MSKU", ContainerQuery(firstRequest, "search"));
        Assert.Equal("42R1", ContainerQuery(firstRequest, "containerType"));
        Assert.Equal("NLRTM", ContainerQuery(firstRequest, "portOfLoadingCode"));
        Assert.Equal("2", ContainerQuery(firstRequest, "pageNumber"));
        Assert.Equal("msku", page.Find("#manifest-search").GetAttribute("value"));
    }

    [Fact]
    public async Task An_edit_that_empties_the_last_page_moves_back_without_losing_the_filters()
    {
        var manifest = NumberedContainers("MSKU", 16);
        respond = request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                manifest.RemoveAt(manifest.Count - 1);

                return Json(
                    HttpStatusCode.OK,
                    """{"id":"70000000-0000-0000-0000-000000000016","containerNumber":"TGHU0000016","containerType":"22G1","portOfLoadingCode":"NLRTM","portOfDischargeCode":"USNYC","weightKg":12500}""");
            }

            return ManifestResponse(request, manifest);
        };

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(15, page.FindAll(".manifest-row").Count));
        page.Find("#manifest-search").Input("MSKU");
        page.WaitForAssertion(
            () => Assert.Equal("MSKU", ContainerQuery(LastContainerRequest(), "search")),
            TimeSpan.FromSeconds(3));
        await page.InvokeAsync(() => page.Find(".manifest-pagination button:last-child").Click());
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".manifest-row")));

        await page.InvokeAsync(() => page.FindAll(".manifest-row button").Single(button => button.TextContent == "Edit").Click());
        await page.InvokeAsync(() => page.Find(".container-edit-modal form").Submit());

        page.WaitForAssertion(() => Assert.Equal(15, page.FindAll(".manifest-row").Count));
        Assert.Equal("1", ContainerQuery(LastContainerRequest(), "pageNumber"));
        Assert.Equal("MSKU", ContainerQuery(LastContainerRequest(), "search"));
        Assert.Empty(page.FindAll(".manifest-pagination"));
    }

    [Fact]
    public async Task A_filtered_manifest_still_completes_operations_for_the_whole_booking()
    {
        var manifest = NumberedContainers("MSKU", 4);
        respond = request => request.RequestUri!.AbsolutePath.EndsWith("/operations", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, Items(OperationJson(OperationId, "Load", "InProgress", StartedStamp)))
            : ManifestResponse(request, manifest);

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".manifest-row").Count));

        page.Find("#manifest-pod").Input("SGSIN");
        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("No containers match these filters", page.Markup, StringComparison.Ordinal);
                Assert.Contains("manifestPod=SGSIN", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(3));

        Assert.Contains("Completing an operation settles all 4 containers on this manifest.", page.Find(".operations-effect").TextContent, StringComparison.Ordinal);
        Assert.False(page.Find(".operation-complete").HasAttribute("disabled"));

        page.Render();
        await page.InvokeAsync(() => page.Find(".operation-complete").Click());

        Assert.Contains("set every container on this booking to Loaded", page.Find(".operation-confirm-question").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opening_and_closing_a_container_control_keeps_the_filters()
    {
        var manifest = NumberedContainers("MSKU", 3);
        respond = request => request.RequestUri!.AbsolutePath.EndsWith("/control", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, $$"""{"control":{{ContainerControlJson(manifest[1].Id, "Pending")}},"history":[]}""")
            : ManifestResponse(request, manifest);

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll(".manifest-row").Count));
        page.Find("#manifest-search").Input("0000002");
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".manifest-row")), TimeSpan.FromSeconds(3));
        var requestsBeforeControl = ContainerRequests().Count;

        await page.InvokeAsync(() => page.Find(".manifest-row .manifest-control-open").Click());
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".container-control-panel")));
        await page.InvokeAsync(() => page.Find(".container-control-modal .operation-modal-close").Click());

        Assert.Empty(page.FindAll(".container-control-panel"));
        Assert.Equal("0000002", page.Find("#manifest-search").GetAttribute("value"));
        Assert.Equal("MSKU0000002", Assert.Single(page.FindAll(".manifest-number")).TextContent);
        Assert.Equal(requestsBeforeControl, ContainerRequests().Count);
    }

    [Fact]
    public void The_manifest_reports_that_it_is_loading()
    {
        var containers = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        respond = request => IsContainersRequest(request)
            ? containers.Task
            : SectionResponse(request, EmptyItems, EmptyItems, Details());

        var page = RenderDetailsPage();

        page.WaitForAssertion(() =>
            Assert.Contains("Loading the container manifest...", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll(".manifest-filters"));

        containers.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ManifestPage([new ManifestContainer("MSKU0000001")], 1, 1, 0), Encoding.UTF8, "application/json"),
        });

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".manifest-row")));
    }

    [Fact]
    public async Task A_running_search_keeps_the_current_rows_and_reports_that_it_is_searching()
    {
        var manifest = NumberedContainers("MSKU", 3);
        var searchSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var searchResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        respond = request =>
        {
            if (IsContainersRequest(request) && ContainerQuery(request.RequestUri!, "search") is not null)
            {
                searchSent.TrySetResult();

                return searchResponse.Task;
            }

            return ManifestResponse(request, manifest);
        };

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll(".manifest-row").Count));

        await page.InvokeAsync(() => page.Find("#manifest-search").Input("0000002"));
        await searchSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Render();

        Assert.Equal("Searching the manifest...", page.Find(".manifest-searching").TextContent.Trim());
        Assert.Equal("true", page.Find(".booking-table-scroll[aria-busy]").GetAttribute("aria-busy"));
        Assert.Equal(3, page.FindAll(".manifest-row").Count);

        searchResponse.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ManifestPage([manifest[1]], 3, 1, 0, 1), Encoding.UTF8, "application/json"),
        });

        page.WaitForAssertion(() =>
            Assert.Equal("MSKU0000002", Assert.Single(page.FindAll(".manifest-number")).TextContent));
        Assert.Empty(page.FindAll(".manifest-searching"));
    }

    [Fact]
    public async Task An_import_refreshes_the_counts_and_keeps_the_filters()
    {
        var manifest = NumberedContainers("MSKU", 2).Append(new ManifestContainer("TGHU0000001")).ToList();
        respond = request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/containers/import/preview", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{"totalCount":1,"validCount":1,"invalidCount":0,"rows":[]}""");
            }

            if (path.EndsWith("/containers/import", StringComparison.Ordinal))
            {
                var imported = new ManifestContainer("MSKU0000003");
                manifest.Add(imported);

                return Json(HttpStatusCode.OK, $$"""{"createdCount":1,"containerIds":["{{imported.Id}}"]}""");
            }

            return ManifestResponse(request, manifest);
        };

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll(".manifest-row").Count));
        await page.InvokeAsync(() => page.Find("#manifest-search").Input("MSKU"));
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".manifest-row").Count), TimeSpan.FromSeconds(3));

        page.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText("<manifest />", "manifest.xml", contentType: "application/xml"));
        await page.InvokeAsync(() => page.FindAll(".manifest-import button").Single(button => button.TextContent.Contains("Preview manifest", StringComparison.Ordinal)).Click());
        page.WaitForAssertion(() => Assert.Contains("Confirm import", page.Markup, StringComparison.Ordinal));
        await page.InvokeAsync(() => page.FindAll(".manifest-import button").Single(button => button.TextContent.Contains("Confirm import", StringComparison.Ordinal)).Click());

        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll(".manifest-row").Count));
        Assert.Equal("MSKU", ContainerQuery(LastContainerRequest(), "search"));
        Assert.Equal("3 of 4 containers match", page.Find(".manifest-results").TextContent.Trim());
        Assert.Contains("4", page.Find(".kpi-containers .booking-kpi-value").TextContent, StringComparison.Ordinal);
        Assert.Equal("MSKU", page.Find(".manifest-active-filter").TextContent.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]);
    }

    [Fact]
    public async Task Completing_an_operation_refreshes_the_counts_and_keeps_the_filters()
    {
        var manifest = NumberedContainers("MSKU", 3).Append(new ManifestContainer("TGHU0000001")).ToList();
        var completed = false;
        respond = request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/complete", StringComparison.Ordinal))
            {
                completed = true;
                manifest = manifest.Select(container => container with { Status = "Loaded" }).ToList();

                return Json(HttpStatusCode.OK, Transition("Completed", StartedStamp, FinishedStamp));
            }

            if (path.EndsWith("/operations", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, Items(completed
                    ? OperationJson(OperationId, "Load", "Completed", StartedStamp, FinishedStamp)
                    : OperationJson(OperationId, "Load", "InProgress", StartedStamp)));
            }

            return ManifestResponse(request, manifest);
        };

        var page = RenderDetailsPage();
        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".manifest-row").Count));
        await page.InvokeAsync(() => page.Find("#manifest-search").Input("TGHU"));
        page.WaitForAssertion(
            () =>
            {
                Assert.Single(page.FindAll(".manifest-row"));
                Assert.Contains("manifestSearch=TGHU", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(3));

        page.Render();
        await page.InvokeAsync(() => page.Find(".operation-complete").Click());
        await page.InvokeAsync(() => page.Find(".operation-confirm").Click());

        page.WaitForAssertion(() => Assert.Equal("4 of 4 loaded", page.Find(".manifest-progress").TextContent.Trim()));
        Assert.Equal("TGHU", ContainerQuery(LastContainerRequest(), "search"));
        Assert.Equal("TGHU0000001", Assert.Single(page.FindAll(".manifest-number")).TextContent);
        Assert.Contains("Loaded", page.Find(".manifest-row .cargo-status").TextContent, StringComparison.Ordinal);
    }

    private sealed record ManifestContainer(
        string Number,
        string Status = "Registered",
        string ContainerType = "22G1",
        string? PortOfLoadingCode = "NLRTM",
        string? PortOfDischargeCode = "USNYC")
    {
        public string Id { get; } = Guid.NewGuid().ToString();
    }

    private static List<ManifestContainer> NumberedContainers(
        string ownerCode,
        int count,
        string status = "Registered",
        string containerType = "22G1")
    {
        return Enumerable.Range(1, count)
            .Select(ordinal => new ManifestContainer($"{ownerCode}{ordinal:D7}", status, containerType))
            .ToList();
    }

    private void RespondWithManifest(IReadOnlyList<ManifestContainer> manifest)
    {
        respond = request => ManifestResponse(request, manifest);
    }

    private static Task<HttpResponseMessage> ManifestResponse(
        HttpRequestMessage request,
        IReadOnlyList<ManifestContainer> manifest)
    {
        if (!IsContainersRequest(request))
        {
            return SectionResponse(request, EmptyItems, EmptyItems, Details());
        }

        var uri = request.RequestUri!;
        var search = ContainerQuery(uri, "search");
        var status = ContainerQuery(uri, "status");
        var containerType = ContainerQuery(uri, "containerType");
        var portOfLoading = ContainerQuery(uri, "portOfLoadingCode");
        var portOfDischarge = ContainerQuery(uri, "portOfDischargeCode");
        var pageNumber = int.Parse(ContainerQuery(uri, "pageNumber") ?? "1", System.Globalization.CultureInfo.InvariantCulture);

        var matching = manifest
            .Where(container => search is null || container.Number.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(container => status is null || container.Status == status)
            .Where(container => containerType is null || container.ContainerType == containerType)
            .Where(container => portOfLoading is null || container.PortOfLoadingCode == portOfLoading)
            .Where(container => portOfDischarge is null || container.PortOfDischargeCode == portOfDischarge)
            .OrderBy(container => container.Number, StringComparer.Ordinal)
            .ToList();

        return Json(
            HttpStatusCode.OK,
            ManifestPage(
                matching.Skip((pageNumber - 1) * 15).Take(15).ToList(),
                manifest.Count,
                pageNumber,
                manifest.Count(container => container.Status == "Loaded"),
                matching.Count));
    }

    private static string ManifestPage(
        IReadOnlyList<ManifestContainer> items,
        int totalCount,
        int pageNumber,
        int loadedCount,
        int? matchingCount = null)
    {
        var rows = items.Select(container => $$"""
            {"containerId":"{{container.Id}}","containerNumber":"{{container.Number}}","containerType":"{{container.ContainerType}}","status":"{{container.Status}}","cargoBookingId":"{{BookingId}}","vesselId":null,"portOfLoadingCode":"{{container.PortOfLoadingCode}}","portOfDischargeCode":"{{container.PortOfDischargeCode}}","weightKg":12500}
            """);

        return $$"""{"items":[{{string.Join(',', rows)}}],"totalCount":{{totalCount}},"pageNumber":{{pageNumber}},"pageSize":15,"loadedCount":{{loadedCount}},"matchingCount":{{matchingCount ?? items.Count}}}""";
    }

    private static bool IsContainersRequest(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath.EndsWith("/containers", StringComparison.Ordinal);
    }

    private List<Uri> ContainerRequests()
    {
        return requestedUris
            .Where(uri => uri.AbsolutePath.EndsWith("/containers", StringComparison.Ordinal))
            .ToList();
    }

    private Uri LastContainerRequest()
    {
        return ContainerRequests()[^1];
    }

    private static string? ContainerQuery(Uri uri, string key)
    {
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).TryGetValue(key, out var value)
            ? value.ToString()
            : null;
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
        string[]? trackingEvents = null,
        string[]? containerControls = null)
    {
        return $$"""
            {"cargoBookingId":"{{BookingId}}","bookingNumber":"{{bookingNumber}}","voyageId":"{{VoyageId}}","description":"{{description}}","cargoType":"{{cargoType}}","weightKg":{{weightKg}},"packageCount":{{packageCount}},"isHazardous":{{(isHazardous ? "true" : "false")}},"status":"{{status}}","nextStatus":{{(nextStatus is null ? "null" : $"\"{nextStatus}\"")}},"createdUtc":"2026-08-11T09:42:00Z","safetyInfo":{{safetyInfo ?? "null"}},"trackingEvents":[{{string.Join(',', trackingEvents ?? [])}}],"containerControls":[{{string.Join(',', containerControls ?? [])}}]}
            """;
    }

    private static string ContainerControlJson(
        string containerId,
        string state,
        string containerNumber = "MSKU1234567",
        string? releaseBlockedReason = "it has no intact seal")
    {
        var blockedReason = releaseBlockedReason is null ? "null" : $"\"{releaseBlockedReason}\"";

        return $$"""
            {"containerId":"{{containerId}}","containerNumber":"{{containerNumber}}","containerType":"20GP","isReefer":false,"sealNumber":null,"sealStatus":"None","state":"{{state}}","latestInspectionResult":null,"latestInspectionNotes":null,"latestInspectionTemperatureCelsius":null,"latestInspectedAtUtc":null,"latestInspectedByUserId":null,"stateReason":null,"stateChangedByUserId":null,"stateChangedAtUtc":null,"canRelease":false,"releaseBlockedReason":{{blockedReason}}}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });
    }

    private static bool IsSafetyRoute(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath.EndsWith("/safety-information", StringComparison.Ordinal);
    }

    private static string SafetyView(string declaration, string timeline = "[]")
    {
        return $$"""{"declaration":{{declaration}},"timeline":{{timeline}}}""";
    }

    private static Task<HttpResponseMessage> SafetyResponse(string? declaration, string timeline = "[]")
    {
        return declaration is null
            ? Json(HttpStatusCode.NotFound, SafetyNotFound)
            : Json(HttpStatusCode.OK, SafetyView(declaration, timeline));
    }

    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> RespondWith(
        string details,
        string? declaration = null,
        string timeline = "[]")
    {
        return request => IsSafetyRoute(request)
            ? SafetyResponse(declaration, timeline)
            : Json(HttpStatusCode.OK, details);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request, cancellationToken);
        }
    }
}
