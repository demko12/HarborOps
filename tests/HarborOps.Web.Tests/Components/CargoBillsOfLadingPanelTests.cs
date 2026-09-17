using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Cargo;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class CargoBillsOfLadingPanelTests : BunitContext
{
    private const string BookingId = "20000000-0000-0000-0000-000000000001";
    private const string BillId = "80000000-0000-0000-0000-000000000008";
    private const string ContainerId = "90000000-0000-0000-0000-000000000009";
    private const string OtherContainerId = "90000000-0000-0000-0000-00000000000b";

    private const string ContainersJson = """
        {"items":[{"containerId":"90000000-0000-0000-0000-000000000009","containerNumber":"MSKU1111111","containerType":"40HC","status":"Loaded","cargoBookingId":"20000000-0000-0000-0000-000000000001","vesselId":null},{"containerId":"90000000-0000-0000-0000-00000000000b","containerNumber":"TCLU2222222","containerType":"20GP","status":"Loaded","cargoBookingId":"20000000-0000-0000-0000-000000000001","vesselId":null}]}
        """;

    private const string EmptyListJson = """
        {"cargoBookingId":"20000000-0000-0000-0000-000000000001","bookingNumber":"BK-2026-0143","bookingStatus":"Loaded","billsOfLading":[]}
        """;

    private const string ListJson = """
        {"cargoBookingId":"20000000-0000-0000-0000-000000000001","bookingNumber":"BK-2026-0143","bookingStatus":"Loaded","billsOfLading":[{"billOfLadingId":"80000000-0000-0000-0000-000000000008","billNumber":"BL-2026-0001","status":"Draft","shipperName":"Adriatic Trade","consigneeName":"Nordic Imports","portOfLoading":"Rotterdam","portOfDischarge":"Bar","containerCount":1,"issuedByUserId":null,"issuedAtUtc":null,"voidedAtUtc":null,"createdUtc":"2026-09-09T08:00:00Z"}]}
        """;

    private const string DraftBill = """
        {"billOfLadingId":"80000000-0000-0000-0000-000000000008","cargoBookingId":"20000000-0000-0000-0000-000000000001","billNumber":"BL-2026-0001","status":"Draft","shipperName":"Adriatic Trade","shipperAddress":"Bulevar Revolucije 1, Bar","consigneeName":"Nordic Imports","consigneeAddress":"Hamngatan 4, Goteborg","notifyPartyName":null,"notifyPartyAddress":null,"placeOfReceipt":"Bar","portOfLoading":"Rotterdam","portOfDischarge":"Bar","placeOfDelivery":"Podgorica","freightTerms":"Prepaid","cargoMarks":"ADT/2026/0001","cargoDescription":"Machine parts, palletised","packageCount":480,"weightKg":12400.000,"issuePlace":"Bar","requestedIssueDate":"2026-09-09","issuedByUserId":null,"issuedAtUtc":null,"voidedByUserId":null,"voidedAtUtc":null,"voidReason":null,"snapshotBookingNumber":null,"snapshotVoyageId":null,"snapshotCargoType":null,"snapshotBookingDescription":null,"snapshotBookingPackageCount":null,"snapshotBookingWeightKg":null,"snapshotBookingStatus":null,"isEditable":true,"canIssue":true,"issueBlockedReason":null,"createdUtc":"2026-09-09T08:00:00Z","containers":[{"containerId":"90000000-0000-0000-0000-000000000009","containerNumber":"MSKU1111111","containerType":"40HC","isSnapshotted":false}]}
        """;

    private const string BlockedBill = """
        {"billOfLadingId":"80000000-0000-0000-0000-000000000008","cargoBookingId":"20000000-0000-0000-0000-000000000001","billNumber":"BL-2026-0001","status":"Draft","shipperName":"Adriatic Trade","shipperAddress":"Bulevar Revolucije 1, Bar","consigneeName":"Nordic Imports","consigneeAddress":"Hamngatan 4, Goteborg","notifyPartyName":null,"notifyPartyAddress":null,"placeOfReceipt":"Bar","portOfLoading":"Rotterdam","portOfDischarge":"Bar","placeOfDelivery":"Podgorica","freightTerms":"Prepaid","cargoMarks":"ADT/2026/0001","cargoDescription":"Machine parts, palletised","packageCount":480,"weightKg":12400.000,"issuePlace":"Bar","requestedIssueDate":"2026-09-09","issuedByUserId":null,"issuedAtUtc":null,"voidedByUserId":null,"voidedAtUtc":null,"voidReason":null,"snapshotBookingNumber":null,"snapshotVoyageId":null,"snapshotCargoType":null,"snapshotBookingDescription":null,"snapshotBookingPackageCount":null,"snapshotBookingWeightKg":null,"snapshotBookingStatus":null,"isEditable":true,"canIssue":false,"issueBlockedReason":"its booking has not reached the Loaded status","createdUtc":"2026-09-09T08:00:00Z","containers":[]}
        """;

    private const string IssuedBill = """
        {"billOfLadingId":"80000000-0000-0000-0000-000000000008","cargoBookingId":"20000000-0000-0000-0000-000000000001","billNumber":"BL-2026-0001","status":"Issued","shipperName":"Adriatic Trade","shipperAddress":"Bulevar Revolucije 1, Bar","consigneeName":"Nordic Imports","consigneeAddress":"Hamngatan 4, Goteborg","notifyPartyName":null,"notifyPartyAddress":null,"placeOfReceipt":"Bar","portOfLoading":"Rotterdam","portOfDischarge":"Bar","placeOfDelivery":"Podgorica","freightTerms":"Prepaid","cargoMarks":"ADT/2026/0001","cargoDescription":"Machine parts, palletised","packageCount":480,"weightKg":12400.000,"issuePlace":"Bar","requestedIssueDate":"2026-09-09","issuedByUserId":"70000000-0000-0000-0000-000000000006","issuedAtUtc":"2026-09-09T10:30:00Z","voidedByUserId":null,"voidedAtUtc":null,"voidReason":null,"snapshotBookingNumber":"BK-2026-0143","snapshotVoyageId":"30000000-0000-0000-0000-000000000002","snapshotCargoType":"General cargo","snapshotBookingDescription":"Machine parts","snapshotBookingPackageCount":480,"snapshotBookingWeightKg":12400.000,"snapshotBookingStatus":"Loaded","isEditable":false,"canIssue":false,"issueBlockedReason":"it is already issued","createdUtc":"2026-09-09T08:00:00Z","containers":[{"containerId":"90000000-0000-0000-0000-000000000009","containerNumber":"MSKU1111111","containerType":"40HC","isSnapshotted":true}]}
        """;

    private const string VoidedBill = """
        {"billOfLadingId":"80000000-0000-0000-0000-000000000008","cargoBookingId":"20000000-0000-0000-0000-000000000001","billNumber":"BL-2026-0001","status":"Voided","shipperName":"Adriatic Trade","shipperAddress":"Bulevar Revolucije 1, Bar","consigneeName":"Nordic Imports","consigneeAddress":"Hamngatan 4, Goteborg","notifyPartyName":null,"notifyPartyAddress":null,"placeOfReceipt":"Bar","portOfLoading":"Rotterdam","portOfDischarge":"Bar","placeOfDelivery":"Podgorica","freightTerms":"Prepaid","cargoMarks":"ADT/2026/0001","cargoDescription":"Machine parts, palletised","packageCount":480,"weightKg":12400.000,"issuePlace":"Bar","requestedIssueDate":"2026-09-09","issuedByUserId":"70000000-0000-0000-0000-000000000006","issuedAtUtc":"2026-09-09T10:30:00Z","voidedByUserId":"70000000-0000-0000-0000-000000000006","voidedAtUtc":"2026-09-09T14:45:00Z","voidReason":"Wrong consignee","snapshotBookingNumber":"BK-2026-0143","snapshotVoyageId":"30000000-0000-0000-0000-000000000002","snapshotCargoType":"General cargo","snapshotBookingDescription":"Machine parts","snapshotBookingPackageCount":480,"snapshotBookingWeightKg":12400.000,"snapshotBookingStatus":"Loaded","isEditable":false,"canIssue":false,"issueBlockedReason":"it is already voided","createdUtc":"2026-09-09T08:00:00Z","containers":[{"containerId":"90000000-0000-0000-0000-000000000009","containerNumber":"MSKU1111111","containerType":"40HC","isSnapshotted":true}]}
        """;

    private const string HistoryJson = """
        [{"billOfLadingEventId":"a0000000-0000-0000-0000-00000000000a","action":"Drafted","fromStatus":"Draft","toStatus":"Draft","notes":null,"actorUserId":"70000000-0000-0000-0000-000000000006","occurredUtc":"2026-09-09T08:00:00Z"},{"billOfLadingEventId":"a0000000-0000-0000-0000-00000000000c","action":"Issued","fromStatus":"Draft","toStatus":"Issued","notes":null,"actorUserId":"70000000-0000-0000-0000-000000000006","occurredUtc":"2026-09-09T10:30:00Z"}]
        """;

    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<string> requestBodies = [];

    private string listJson = ListJson;
    private string billJson = DraftBill;
    private Func<HttpRequestMessage, Task<HttpResponseMessage>>? overrideRespond;

    public CargoBillsOfLadingPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_list_is_requested_for_the_booking()
    {
        RenderPanel();

        Assert.Contains(
            requests,
            request => request.RequestUri!.AbsolutePath
                == $"/api/cargo/bookings/{BookingId}/bills-of-lading");
    }

    [Fact]
    public void An_empty_booking_says_so()
    {
        listJson = EmptyListJson;

        var panel = RenderPanel();

        Assert.Contains(
            "No bill of lading has been prepared",
            panel.Find(".bills-empty").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_shows_the_number_status_and_route()
    {
        var panel = RenderPanel();

        Assert.Contains("BL-2026-0001", panel.Find(".bill-list-number").TextContent, StringComparison.Ordinal);
        Assert.Contains("Draft", panel.Find(".bill-status").TextContent, StringComparison.Ordinal);
        Assert.Contains("Rotterdam", panel.Find(".bill-list-route").TextContent, StringComparison.Ordinal);
        Assert.Contains("1 container", panel.Find(".bill-list-containers").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_a_bill_shows_its_parties_and_cargo()
    {
        var panel = RenderPanel();

        OpenBill(panel);

        Assert.Contains("Adriatic Trade", panel.Find(".bill-facts").TextContent, StringComparison.Ordinal);
        Assert.Contains("Nordic Imports", panel.Find(".bill-facts").TextContent, StringComparison.Ordinal);
        Assert.Contains("Machine parts", panel.Find(".bill-facts").TextContent, StringComparison.Ordinal);
        Assert.Contains("MSKU1111111", panel.Find(".bill-container-list").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blocked_draft_explains_why_it_cannot_be_issued()
    {
        billJson = BlockedBill;

        var panel = RenderPanel();

        OpenBill(panel);

        Assert.Contains(
            "must reach the Loaded status",
            panel.Find(".bill-blocked").TextContent,
            StringComparison.Ordinal);
        Assert.True(panel.Find(".bill-issue").HasAttribute("disabled"));
    }

    [Fact]
    public void An_issued_bill_is_shown_as_immutable_with_its_snapshot()
    {
        billJson = IssuedBill;

        var panel = RenderPanel();

        OpenBill(panel);

        Assert.Contains(
            "can no longer be edited",
            panel.Find(".bill-immutable").TextContent,
            StringComparison.Ordinal);
        Assert.Contains("BK-2026-0143", panel.Find(".bill-audit").TextContent, StringComparison.Ordinal);
        Assert.Contains("as issued", panel.Find(".bill-container-snapshot").TextContent, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".bill-edit"));
    }

    [Fact]
    public void A_voided_bill_shows_its_reason_and_keeps_the_snapshot()
    {
        billJson = VoidedBill;

        var panel = RenderPanel();

        OpenBill(panel);

        Assert.Contains("Wrong consignee", panel.Find(".bill-void-reason").TextContent, StringComparison.Ordinal);
        Assert.Contains("Create a replacement", panel.Find(".bill-void-hint").TextContent, StringComparison.Ordinal);
        Assert.Contains("BK-2026-0143", panel.Find(".bill-audit").TextContent, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".bill-void"));
        Assert.NotEmpty(panel.FindAll(".bill-replace"));
    }

    [Fact]
    public void A_voided_bill_offers_a_prefilled_replacement()
    {
        billJson = VoidedBill;

        var panel = RenderPanel();

        OpenBill(panel);
        panel.Find(".bill-replace").Click();

        Assert.Contains(
            "New bill of lading",
            panel.Find("#bill-form-title").TextContent,
            StringComparison.Ordinal);
        Assert.True(string.IsNullOrEmpty(panel.Find("#bill-number").GetAttribute("value")));
        Assert.Equal("Adriatic Trade", panel.Find("#bill-shipper").GetAttribute("value"));
        Assert.Equal("Rotterdam", panel.Find("#bill-loading").GetAttribute("value"));
        Assert.Contains(
            "1",
            panel.Find(".bill-summary").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Only_a_voided_bill_offers_a_replacement()
    {
        var panel = RenderPanel();

        OpenBill(panel);

        Assert.Empty(panel.FindAll(".bill-replace"));
    }

    [Fact]
    public void A_reader_is_offered_no_action()
    {
        var panel = RenderPanel(CurrentUserAuthorizationState.CargoReadPermission);

        OpenBill(panel);

        Assert.Empty(panel.FindAll(".bill-new"));
        Assert.Empty(panel.FindAll(".bill-edit"));
        Assert.Empty(panel.FindAll(".bill-issue"));
        Assert.Contains(
            "not change them",
            panel.Find(".bill-readonly").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_reader_can_still_open_the_printable_bill()
    {
        var panel = RenderPanel(CurrentUserAuthorizationState.CargoReadPermission);

        OpenBill(panel);

        Assert.Equal(
            $"/cargo/{BookingId}/bills-of-lading/{BillId}/print",
            panel.Find(".bill-print").GetAttribute("href"));
    }

    [Fact]
    public void The_new_draft_form_offers_the_booking_containers()
    {
        var panel = RenderPanel();

        panel.Find(".bill-new").Click();

        Assert.Contains("MSKU1111111", panel.Find(".bill-container-picker").TextContent, StringComparison.Ordinal);
        Assert.Contains("TCLU2222222", panel.Find(".bill-container-picker").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Selecting_containers_updates_the_summary()
    {
        var panel = RenderPanel();

        panel.Find(".bill-new").Click();

        Assert.Contains("0", panel.Find(".bill-summary").TextContent, StringComparison.Ordinal);

        panel.FindAll(".bill-container-option input")[0].Change(true);

        Assert.Contains("1", panel.Find(".bill-summary").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_form_is_rejected_before_any_request()
    {
        var panel = RenderPanel();
        var before = requests.Count;

        panel.Find(".bill-new").Click();
        panel.Find(".bill-form").Submit();

        Assert.Equal(before, requests.Count);
        Assert.Contains("required", panel.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Creating_a_draft_posts_to_the_booking()
    {
        var panel = RenderPanel();

        panel.Find(".bill-new").Click();
        FillForm(panel);
        panel.Find(".bill-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath
                    == $"/api/cargo/bookings/{BookingId}/bills-of-lading"));
        Assert.Contains(
            requestBodies,
            body => body.Contains("BL-2026-0002", StringComparison.Ordinal));
    }

    [Fact]
    public void Editing_a_draft_preloads_its_values()
    {
        var panel = RenderPanel();

        OpenBill(panel);
        panel.Find(".bill-edit").Click();

        Assert.Equal("BL-2026-0001", panel.Find("#bill-number").GetAttribute("value"));
        Assert.Equal("Adriatic Trade", panel.Find("#bill-shipper").GetAttribute("value"));
    }

    [Fact]
    public void The_form_opens_in_a_modal_with_a_close_button()
    {
        var panel = RenderPanel();

        panel.Find(".bill-new").Click();

        Assert.NotEmpty(panel.FindAll(".bill-form-modal"));
        Assert.Equal(
            "dialog",
            panel.Find(".bill-form-modal").GetAttribute("role"));
        Assert.Contains(
            "New bill of lading",
            panel.Find("#bill-form-title").TextContent,
            StringComparison.Ordinal);
        Assert.NotEmpty(panel.FindAll(".bill-form-modal .operation-modal-close"));
    }

    [Fact]
    public void Closing_the_modal_leaves_the_form()
    {
        var panel = RenderPanel();

        panel.Find(".bill-new").Click();
        panel.Find(".bill-form-modal .operation-modal-close").Click();

        Assert.Empty(panel.FindAll(".bill-form-modal"));
    }

    [Fact]
    public void Only_the_unclear_fields_carry_an_example()
    {
        var panel = RenderPanel();

        panel.Find(".bill-new").Click();

        Assert.Equal("e.g. BL-2026-0001", panel.Find("#bill-number").GetAttribute("placeholder"));
        Assert.Equal("e.g. ADT/2026/0001", panel.Find("#bill-marks").GetAttribute("placeholder"));
        Assert.StartsWith(
            "e.g.",
            panel.Find("#bill-notify").GetAttribute("placeholder"),
            StringComparison.Ordinal);

        Assert.Null(panel.Find("#bill-shipper").GetAttribute("placeholder"));
        Assert.Equal("Search ports", panel.Find("#bill-loading").GetAttribute("placeholder"));
        Assert.Null(panel.Find("#bill-packages").GetAttribute("placeholder"));
    }

    [Fact]
    public void The_port_fields_suggest_ports_already_used_on_this_booking()
    {
        var panel = RenderPanel();

        panel.Find(".bill-new").Click();

        Assert.Equal("bill-known-ports", panel.Find("#bill-receipt").GetAttribute("list"));
        Assert.Equal("bill-known-ports", panel.Find("#bill-delivery").GetAttribute("list"));

        var options = panel.FindAll("#bill-known-ports option").Select(option => option.GetAttribute("value"));

        Assert.Equal(["Bar", "Rotterdam"], options);
    }

    [Fact]
    public void Cancelling_the_form_returns_without_a_request()
    {
        var panel = RenderPanel();

        panel.Find(".bill-new").Click();
        var before = requests.Count;
        panel.Find(".bill-cancel").Click();

        Assert.Equal(before, requests.Count);
        Assert.Empty(panel.FindAll(".bill-form"));
    }

    [Fact]
    public void Issuing_asks_for_confirmation_before_it_posts()
    {
        var panel = RenderPanel();

        OpenBill(panel);
        var before = requests.Count;
        panel.Find(".bill-issue").Click();

        Assert.Equal(before, requests.Count);
        Assert.Contains(
            "can no longer be edited",
            panel.Find(".bill-confirm-question").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Confirming_the_issue_posts_to_the_issue_route()
    {
        var panel = RenderPanel();

        OpenBill(panel);
        panel.Find(".bill-issue").Click();
        panel.Find(".bill-confirm-actions .booking-submit").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.RequestUri!.AbsolutePath
                == $"/api/cargo/bills-of-lading/{BillId}/issue"));
    }

    [Fact]
    public void Cancelling_the_confirmation_posts_nothing()
    {
        var panel = RenderPanel();

        OpenBill(panel);
        panel.Find(".bill-issue").Click();
        var before = requests.Count;
        panel.Find(".bill-confirm-actions .bill-cancel").Click();

        Assert.Equal(before, requests.Count);
        Assert.Empty(panel.FindAll(".bill-confirm-modal"));
    }

    [Fact]
    public void Voiding_without_a_reason_is_rejected_before_any_request()
    {
        billJson = IssuedBill;

        var panel = RenderPanel();

        OpenBill(panel);
        panel.Find(".bill-void").Click();
        var before = requests.Count;
        panel.Find(".bill-confirm-actions .booking-submit").Click();

        Assert.Equal(before, requests.Count);
        Assert.Contains(
            "reason is required",
            panel.Find(".bill-confirm-field .booking-form-error").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Confirming_the_void_posts_the_reason()
    {
        billJson = IssuedBill;

        var panel = RenderPanel();

        OpenBill(panel);
        panel.Find(".bill-void").Click();
        panel.Find("#bill-void-reason").Change("Wrong consignee");
        panel.Find(".bill-confirm-actions .booking-submit").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.RequestUri!.AbsolutePath
                == $"/api/cargo/bills-of-lading/{BillId}/void"));
        Assert.Contains(
            requestBodies,
            body => body.Contains("Wrong consignee", StringComparison.Ordinal));
    }

    [Fact]
    public void A_conflict_is_shown_and_the_bill_is_reloaded()
    {
        var panel = RenderPanel();

        OpenBill(panel);
        overrideRespond = request => request.RequestUri!.AbsolutePath.EndsWith("/issue", StringComparison.Ordinal)
            ? Json(HttpStatusCode.Conflict, """{"title":"Bill of lading transition is not allowed.","status":409}""")
            : Respond(request);

        panel.Find(".bill-issue").Click();
        panel.Find(".bill-confirm-actions .booking-submit").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            "not allowed",
            panel.Find(".bill-confirm-modal .booking-form-error").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_history_lists_every_recorded_action()
    {
        var panel = RenderPanel();

        OpenBill(panel);
        panel.Find(".bill-history-open").Click();

        var timeline = panel.Find(".bill-timeline").TextContent;

        Assert.Contains("Draft created", timeline, StringComparison.Ordinal);
        Assert.Contains("Issued", timeline, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_can_be_retried()
    {
        overrideRespond = _ => Json(
            HttpStatusCode.ServiceUnavailable,
            """{"title":"Unable to load the bills of lading","status":503}""");

        var panel = RenderPanel();

        Assert.NotEmpty(panel.FindAll("button"));

        overrideRespond = null;
        panel.Find(".api-error-state button").Click();

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".bill-list-number")));
    }

    private void OpenBill(IRenderedComponent<OverlayHost> panel)
    {
        panel.Find(".bill-list-button").Click();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".bill-detail")));
    }

    private static void FillForm(IRenderedComponent<OverlayHost> panel)
    {
        panel.Find("#bill-number").Change("BL-2026-0002");
        panel.Find("#bill-shipper").Change("Adriatic Trade");
        panel.Find("#bill-shipper-address").Change("Bulevar Revolucije 1, Bar");
        panel.Find("#bill-consignee").Change("Nordic Imports");
        panel.Find("#bill-consignee-address").Change("Hamngatan 4, Goteborg");
        panel.Find("#bill-receipt").Change("Bar");
        panel.Find("#bill-loading").Input("Rotterdam");
        panel.Find("#bill-discharge").Input("Bar");
        panel.Find("#bill-delivery").Change("Podgorica");
        panel.Find("#bill-marks").Change("ADT/2026/0002");
        panel.Find("#bill-description").Change("Machine parts, palletised");
        panel.Find("#bill-packages").Change(480);
        panel.Find("#bill-weight").Change(12400m);
        panel.Find("#bill-issue-place").Change("Bar");
        panel.FindAll(".bill-container-option input")[0].Change(true);
    }

    private Task<HttpResponseMessage> Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/ports/lookup", StringComparison.Ordinal))
        {
            return Json(
                HttpStatusCode.OK,
                """{"items":[{"portId":"c0000000-0000-0000-0000-00000000000c","name":"Rotterdam","country":"Netherlands","unLocode":"NLRTM"}]}""");
        }

        if (path.EndsWith("/containers", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, ContainersJson);
        }

        if (path.EndsWith("/bills-of-lading", StringComparison.Ordinal)
            && request.Method == HttpMethod.Get)
        {
            return Json(HttpStatusCode.OK, listJson);
        }

        if (path.EndsWith($"/bills-of-lading/{BillId}", StringComparison.Ordinal)
            && request.Method == HttpMethod.Get)
        {
            return Json(HttpStatusCode.OK, $$"""{"billOfLading":{{billJson}},"history":{{HistoryJson}}}""");
        }

        return Json(HttpStatusCode.OK, billJson);
    }

    private IRenderedComponent<OverlayHost> RenderPanel(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, _) =>
        {
            requests.Add(request);
            requestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(CancellationToken.None));

            return overrideRespond is null
                ? await Respond(request)
                : await overrideRespond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CargoBookingsApiClient(httpClient));
        Services.AddSingleton(new VoyagesApiClient(httpClient));
        ConfigureAuthorization(permissions.Length == 0
            ? [CurrentUserAuthorizationState.CargoManagePermission]
            : permissions);

        var panel = Render<OverlayHost>(parameters => parameters
            .Add(component => component.BookingId, Guid.Parse(BookingId)));
        panel.WaitForAssertion(() => Assert.NotEmpty(requests));

        return panel;
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

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    internal sealed class OverlayHost : ComponentBase
    {
        [Parameter]
        public Guid BookingId { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.OpenComponent<CargoBillsOfLadingPanel>(0);
            builder.AddComponentParameter(
                1,
                nameof(CargoBillsOfLadingPanel.BookingId),
                BookingId);
            builder.CloseComponent();

            builder.OpenComponent<SectionOutlet>(2);
            builder.AddComponentParameter(
                3,
                nameof(SectionOutlet.SectionName),
                "harborops-overlay");
            builder.CloseComponent();
        }
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
