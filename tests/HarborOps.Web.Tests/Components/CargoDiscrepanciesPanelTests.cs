using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Cargo;
using HarborOps.Web.Components.Pages.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class CargoDiscrepanciesPanelTests : BunitContext
{
    private const string BookingId = "20000000-0000-0000-0000-000000000001";
    private const string DiscrepancyId = "d0000000-0000-0000-0000-000000000001";
    private const string ContainerId = "70000000-0000-0000-0000-000000000006";
    private const string ExcursionId = "90000000-0000-0000-0000-000000000001";

    private const string OnePage = """
        {"items":[{"id":"d0000000-0000-0000-0000-000000000001","cargoBookingId":"20000000-0000-0000-0000-000000000001","containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","type":"SealMismatch","severity":"High","description":"Seal differs","observedAtUtc":"2026-09-29T07:30:00Z","reportedByUserId":"50000000-0000-0000-0000-000000000005","reportedAtUtc":"2026-09-29T08:00:00Z","status":"Reported","disposition":null,"resolvedByUserId":null,"resolvedAtUtc":null,"voidedByUserId":null,"voidedAtUtc":null,"sourceExcursionId":null}],"pageNumber":1,"pageSize":10,"totalCount":1,"summary":{"reportedCount":1,"investigatingCount":0,"resolvedCount":2,"voidedCount":1,"openCount":1}}
        """;

    private const string EmptyPage = """
        {"items":[],"pageNumber":1,"pageSize":10,"totalCount":0,"summary":{"reportedCount":0,"investigatingCount":0,"resolvedCount":0,"voidedCount":0,"openCount":0}}
        """;

    private const string ReportedDetails = """
        {"discrepancy":{"id":"d0000000-0000-0000-0000-000000000001","cargoBookingId":"20000000-0000-0000-0000-000000000001","containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","type":"SealMismatch","severity":"High","description":"Seal differs","observedAtUtc":"2026-09-29T07:30:00Z","reportedByUserId":"50000000-0000-0000-0000-000000000005","reportedAtUtc":"2026-09-29T08:00:00Z","status":"Reported","disposition":null,"resolvedByUserId":null,"resolvedAtUtc":null,"voidedByUserId":null,"voidedAtUtc":null,"sourceExcursionId":null},"bookingNumber":"BK-1","bookingStatus":"Unloaded","source":null,"investigationNoteCount":0,"resolutionBlocker":"Start the investigation before resolving the discrepancy.","history":[{"id":"e0000000-0000-0000-0000-000000000001","eventType":"Reported","fromStatus":null,"toStatus":"Reported","text":null,"actorUserId":"50000000-0000-0000-0000-000000000005","occurredAtUtc":"2026-09-29T08:00:00Z"}]}
        """;

    private const string InvestigatingDetails = """
        {"discrepancy":{"id":"d0000000-0000-0000-0000-000000000001","cargoBookingId":"20000000-0000-0000-0000-000000000001","containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","type":"TemperatureRelated","severity":"High","description":"Warm cargo","observedAtUtc":"2026-09-29T07:30:00Z","reportedByUserId":"50000000-0000-0000-0000-000000000005","reportedAtUtc":"2026-09-29T08:00:00Z","status":"Investigating","disposition":null,"resolvedByUserId":null,"resolvedAtUtc":null,"voidedByUserId":null,"voidedAtUtc":null,"sourceExcursionId":"90000000-0000-0000-0000-000000000001"},"bookingNumber":"BK-1","bookingStatus":"Unloaded","source":{"excursionId":"90000000-0000-0000-0000-000000000001","containerId":"70000000-0000-0000-0000-000000000006","direction":"Above","temperatureCelsius":7.5,"thresholdMinimumCelsius":-2,"thresholdMaximumCelsius":4,"measuredAtUtc":"2026-09-29T07:30:00Z","detectedAtUtc":"2026-09-29T07:35:00Z","currentStatus":"Open","currentDisposition":null,"supersededByExcursionId":null,"blocksResolution":true},"investigationNoteCount":1,"resolutionBlocker":"The linked temperature excursion is still open. Resolve it or correct its reading first.","history":[{"id":"e0000000-0000-0000-0000-000000000001","eventType":"Reported","fromStatus":null,"toStatus":"Reported","text":null,"actorUserId":"50000000-0000-0000-0000-000000000005","occurredAtUtc":"2026-09-29T08:00:00Z"},{"id":"e0000000-0000-0000-0000-000000000002","eventType":"InvestigationStarted","fromStatus":"Reported","toStatus":"Investigating","text":null,"actorUserId":"50000000-0000-0000-0000-000000000005","occurredAtUtc":"2026-09-29T09:00:00Z"},{"id":"e0000000-0000-0000-0000-000000000003","eventType":"NoteAdded","fromStatus":"Investigating","toStatus":"Investigating","text":"Reefer log <b>checked</b>","actorUserId":"50000000-0000-0000-0000-000000000005","occurredAtUtc":"2026-09-29T09:10:00Z"}]}
        """;

    private const string ResolvableDetails = """
        {"discrepancy":{"id":"d0000000-0000-0000-0000-000000000001","cargoBookingId":"20000000-0000-0000-0000-000000000001","containerId":null,"containerNumber":null,"type":"Shortage","severity":"Medium","description":"Two pallets missing","observedAtUtc":"2026-09-29T07:30:00Z","reportedByUserId":"50000000-0000-0000-0000-000000000005","reportedAtUtc":"2026-09-29T08:00:00Z","status":"Investigating","disposition":null,"resolvedByUserId":null,"resolvedAtUtc":null,"voidedByUserId":null,"voidedAtUtc":null,"sourceExcursionId":null},"bookingNumber":"BK-1","bookingStatus":"Unloaded","source":null,"investigationNoteCount":1,"resolutionBlocker":null,"history":[]}
        """;

    private const string MatchingContainer = """
        {"items":[{"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","containerType":"40HC","status":"Loaded","cargoBookingId":"20000000-0000-0000-0000-000000000001","vesselId":null}],"totalCount":1,"pageNumber":1,"pageSize":15,"loadedCount":1,"matchingCount":1}
        """;

    private const string NoContainers = """
        {"items":[],"totalCount":1,"pageNumber":1,"pageSize":15,"loadedCount":1,"matchingCount":0}
        """;

    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<string> requestBodies = [];

    private string listResponse = OnePage;
    private HttpStatusCode listStatus = HttpStatusCode.OK;
    private string detailsResponse = ReportedDetails;
    private HttpStatusCode detailsStatus = HttpStatusCode.OK;
    private string containersResponse = MatchingContainer;
    private HttpStatusCode actionStatus = HttpStatusCode.OK;
    private string actionResponse = ReportedDetails;

    [Fact]
    public void The_list_shows_the_summary_and_warns_that_completion_is_blocked()
    {
        var panel = RenderPanel(bookingStatus: "Unloaded");

        Assert.Equal("MSKU1234567", panel.Find(".discrepancy-row td:nth-child(4)").TextContent);
        Assert.Contains("Seal mismatch", panel.Find(".discrepancy-row").TextContent, StringComparison.Ordinal);
        Assert.Contains("1 open", panel.Find(".discrepancy-open-count").TextContent, StringComparison.Ordinal);
        Assert.Contains("cannot be completed yet", panel.Find(".discrepancy-blocks").TextContent, StringComparison.Ordinal);
        Assert.Contains($"/api/cargo/bookings/{BookingId}/discrepancies", requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void An_open_discrepancy_does_not_warn_before_the_cargo_is_unloaded()
    {
        var panel = RenderPanel(bookingStatus: "InTransit");

        Assert.Empty(panel.FindAll(".discrepancy-blocks"));
    }

    [Fact]
    public void An_empty_booking_explains_what_is_listed()
    {
        listResponse = EmptyPage;

        var panel = RenderPanel();

        Assert.Contains("No discrepancies", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_list_can_be_retried()
    {
        listStatus = HttpStatusCode.ServiceUnavailable;
        listResponse = """{"title":"Cargo service unavailable"}""";
        var panel = RenderPanel();

        Assert.Contains("Cargo service unavailable", panel.Markup, StringComparison.Ordinal);

        listStatus = HttpStatusCode.OK;
        listResponse = OnePage;
        panel.Find(".api-error-state button").Click();

        panel.WaitForAssertion(() => Assert.Single(panel.FindAll(".discrepancy-row")));
    }

    [Fact]
    public void Filtering_by_status_reloads_the_first_page()
    {
        var panel = RenderPanel();

        panel.Find("#discrepancy-status-filter").Change("Investigating");

        panel.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.RequestUri!.Query.Contains("status=Investigating", StringComparison.Ordinal)
                && request.RequestUri.Query.Contains("pageNumber=1", StringComparison.Ordinal)));
    }

    [Fact]
    public void Filtering_by_type_and_severity_can_be_cleared()
    {
        var panel = RenderPanel();

        panel.Find("#discrepancy-type-filter").Change("SealMismatch");
        panel.Find("#discrepancy-severity-filter").Change("Critical");

        panel.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.RequestUri!.Query.Contains("type=SealMismatch", StringComparison.Ordinal)
                && request.RequestUri.Query.Contains("severity=Critical", StringComparison.Ordinal)));

        panel.Find(".discrepancy-clear-filters").Click();

        panel.WaitForAssertion(() =>
        {
            var last = requests.Last(request => request.RequestUri!.AbsolutePath.EndsWith("/discrepancies", StringComparison.Ordinal));
            Assert.DoesNotContain("type=", last.RequestUri!.Query, StringComparison.Ordinal);
            Assert.DoesNotContain("severity=", last.RequestUri.Query, StringComparison.Ordinal);
            Assert.Empty(panel.FindAll(".discrepancy-clear-filters"));
        });
    }

    [Fact]
    public void Details_show_who_acted_by_name_and_fall_back_to_a_short_id()
    {
        detailsResponse = ReportedDetails
            .Replace("\"reportedAtUtc\"", "\"reportedByName\":\"ana.operator@harborops.test\",\"reportedAtUtc\"", StringComparison.Ordinal);
        var panel = RenderPanel();

        panel.Find(".discrepancy-open").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("by ana.operator@harborops.test", panel.Find(".discrepancy-facts").TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("ana.operator", panel.Find(".discrepancy-history-meta").TextContent, StringComparison.Ordinal);
            Assert.Contains(CargoBookingFormat.ShortUser(Guid.Parse("50000000-0000-0000-0000-000000000005")), panel.Find(".discrepancy-history-meta").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_container_field_opens_a_dropdown_of_the_booking_containers()
    {
        var panel = RenderPanel();

        panel.Find(".discrepancy-register-toggle").Click();
        panel.Find("#discrepancy-container").Focus();

        panel.WaitForAssertion(() =>
        {
            var option = Assert.Single(panel.FindAll(".discrepancy-combobox-option"));
            Assert.Contains("MSKU1234567", option.TextContent, StringComparison.Ordinal);
            Assert.Contains("40HC · Loaded", option.TextContent, StringComparison.Ordinal);
            Assert.Equal("true", panel.Find("#discrepancy-container").GetAttribute("aria-expanded"));
        });

        panel.Find("#discrepancy-container").Input("MSKU12");

        panel.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.RequestUri!.AbsolutePath.EndsWith("/containers", StringComparison.Ordinal)
                && request.RequestUri.Query.Contains("search=MSKU12", StringComparison.Ordinal)));

        panel.Find(".discrepancy-combobox-toggle").Click();

        Assert.Empty(panel.FindAll(".discrepancy-combobox-menu"));
    }

    [Fact]
    public void An_empty_container_search_says_nothing_matches()
    {
        containersResponse = NoContainers;
        var panel = RenderPanel();

        panel.Find(".discrepancy-register-toggle").Click();
        panel.Find(".discrepancy-combobox-toggle").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            "No container on this booking matches.",
            panel.Find(".discrepancy-combobox-menu").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Choosing_a_container_from_the_dropdown_registers_it_without_a_second_lookup()
    {
        actionStatus = HttpStatusCode.Created;
        var panel = RenderPanel();

        panel.Find(".discrepancy-register-toggle").Click();
        panel.Find("#discrepancy-type").Change("SealMismatch");
        panel.Find("#discrepancy-severity").Change("High");
        panel.Find("#discrepancy-container").Focus();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll(".discrepancy-combobox-option")));
        panel.Find(".discrepancy-combobox-option").Click();

        Assert.Equal("MSKU1234567", panel.Find("#discrepancy-container").GetAttribute("value"));
        Assert.Empty(panel.FindAll(".discrepancy-combobox-menu"));

        var lookups = requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/containers", StringComparison.Ordinal));
        panel.Find("#discrepancy-description").Change("Seal differs from the booking");
        panel.Find(".discrepancy-form").Submit();

        panel.WaitForAssertion(() =>
        {
            var index = requests.FindIndex(request => request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/discrepancies", StringComparison.Ordinal));
            Assert.True(index >= 0);
            Assert.Contains($"\"containerId\":\"{ContainerId}\"", requestBodies[index], StringComparison.Ordinal);
        });
        Assert.Equal(lookups, requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/containers", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_keyboard_picks_a_container_and_enter_does_not_submit_the_form()
    {
        var panel = RenderPanel();

        panel.Find(".discrepancy-register-toggle").Click();
        panel.Find("#discrepancy-container").Focus();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll(".discrepancy-combobox-option")));

        panel.Find("#discrepancy-container").KeyDown("ArrowDown");

        Assert.Contains("is-active", panel.Find(".discrepancy-combobox-option").ClassName, StringComparison.Ordinal);
        Assert.Equal("discrepancy-container-option-0", panel.Find("#discrepancy-container").GetAttribute("aria-activedescendant"));

        panel.Find(".discrepancy-form").Submit();

        Assert.Equal("MSKU1234567", panel.Find("#discrepancy-container").GetAttribute("value"));
        Assert.Empty(panel.FindAll(".discrepancy-field-error"));
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);

        panel.Find("#discrepancy-container").Focus();
        panel.Find("#discrepancy-container").KeyDown("Escape");

        Assert.Empty(panel.FindAll(".discrepancy-combobox-menu"));
    }

    [Fact]
    public void A_status_count_filters_the_list_and_clicking_it_again_clears_it()
    {
        var panel = RenderPanel();

        panel.FindAll(".discrepancy-summary-filter")[2].Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("status=Resolved", requests[^1].RequestUri!.Query, StringComparison.Ordinal);
            Assert.Equal("true", panel.FindAll(".discrepancy-summary-filter")[2].GetAttribute("aria-pressed"));
            Assert.Equal("Resolved", panel.Find("#discrepancy-status-filter").GetAttribute("value"));
        });

        panel.FindAll(".discrepancy-summary-filter")[2].Click();

        panel.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("status=", requests[^1].RequestUri!.Query, StringComparison.Ordinal);
            Assert.Equal("false", panel.FindAll(".discrepancy-summary-filter")[2].GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void A_reader_sees_the_history_but_no_actions()
    {
        detailsResponse = InvestigatingDetails;
        var panel = RenderPanel(permissions: CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Empty(panel.FindAll(".discrepancy-register-toggle"));

        panel.Find(".discrepancy-open").Click();

        panel.WaitForAssertion(() => Assert.Equal(3, panel.FindAll(".discrepancy-history-entry").Count));
        Assert.Empty(panel.FindAll(".discrepancy-workflow"));
        Assert.Empty(panel.FindAll(".discrepancy-resolution-blocker"));
    }

    [Fact]
    public void Details_show_the_history_escaped_and_the_open_excursion_blocks_resolution()
    {
        detailsResponse = InvestigatingDetails;
        var panel = RenderPanel();

        panel.Find(".discrepancy-open").Click();

        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".discrepancy-source")));
        Assert.Contains("is-blocking", panel.Find(".discrepancy-source").ClassName, StringComparison.Ordinal);
        Assert.Contains("Open", panel.Find(".discrepancy-source-state").TextContent, StringComparison.Ordinal);
        Assert.Contains("still open", panel.Find(".discrepancy-resolution-blocker").TextContent, StringComparison.Ordinal);
        Assert.True(panel.Find(".discrepancy-resolve-submit").HasAttribute("disabled"));
        Assert.Contains("&lt;b&gt;checked&lt;/b&gt;", panel.Find(".discrepancy-history-text").InnerHtml, StringComparison.Ordinal);
        Assert.Equal("Reported → Investigating", panel.FindAll(".discrepancy-history-transition")[0].TextContent);
    }

    [Fact]
    public void The_source_link_opens_the_container_and_closes_the_details()
    {
        detailsResponse = InvestigatingDetails;
        (Guid ContainerId, string? ContainerNumber)? opened = null;
        var panel = RenderPanel(onOpenContainer: container => opened = container);

        panel.Find(".discrepancy-open").Click();
        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".discrepancy-source-link")));
        panel.Find(".discrepancy-source-link").Click();

        Assert.Equal((Guid.Parse(ContainerId), "MSKU1234567"), opened);
        Assert.Empty(panel.FindAll(".discrepancy-modal"));
    }

    [Fact]
    public void A_reported_discrepancy_explains_and_starts_the_investigation_directly()
    {
        actionResponse = ResolvableDetails;
        var panel = RenderPanel();

        panel.Find(".discrepancy-open").Click();
        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".discrepancy-start")));

        Assert.Contains("moves it to Investigating", panel.Find(".discrepancy-step-start").TextContent, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".discrepancy-resolve-submit"));

        panel.Find(".discrepancy-start").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith($"/discrepancies/{DiscrepancyId}/investigation", StringComparison.Ordinal)));
        panel.WaitForAssertion(() => Assert.Contains("Two pallets missing", panel.Find(".discrepancy-description").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Resolving_needs_a_disposition_and_a_substantive_summary_before_it_is_confirmed()
    {
        detailsResponse = ResolvableDetails;
        var panel = RenderPanel();

        panel.Find(".discrepancy-open").Click();
        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".discrepancy-resolve-submit")));

        panel.Find(".discrepancy-resolve-submit").Click();
        Assert.Contains("Select a disposition", panel.Find(".discrepancy-action-error").TextContent, StringComparison.Ordinal);

        panel.Find("#discrepancy-disposition").Change("Corrected");
        panel.Find("#discrepancy-summary").Change("Too short");
        panel.Find(".discrepancy-resolve-submit").Click();
        Assert.Contains("at least 20 characters", panel.Find(".discrepancy-action-error").TextContent, StringComparison.Ordinal);

        panel.Find("#discrepancy-summary").Change("Pallets found at the neighbouring berth.");
        panel.Find(".discrepancy-resolve-submit").Click();
        panel.Find(".discrepancy-confirm-yes").Click();

        panel.WaitForAssertion(() =>
        {
            var index = requests.FindIndex(request => request.RequestUri!.AbsolutePath.EndsWith("/resolution", StringComparison.Ordinal));
            Assert.True(index >= 0);
            Assert.Contains("\"disposition\":\"Corrected\"", requestBodies[index], StringComparison.Ordinal);
            Assert.Contains("\"summary\":\"Pallets found at the neighbouring berth.\"", requestBodies[index], StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_conflicting_change_is_reported_and_can_be_reloaded()
    {
        detailsResponse = ResolvableDetails;
        actionStatus = HttpStatusCode.Conflict;
        actionResponse = """{"title":"Cargo discrepancy was modified concurrently.","detail":"Reload and try again."}""";
        var panel = RenderPanel();

        panel.Find(".discrepancy-open").Click();
        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".discrepancy-void-submit")));
        panel.Find("#discrepancy-void-reason").Change("Registered twice");
        panel.Find(".discrepancy-void-submit").Click();
        panel.Find(".discrepancy-confirm-yes").Click();

        panel.WaitForAssertion(() => Assert.Contains("modified concurrently", panel.Find(".discrepancy-action-error").TextContent, StringComparison.Ordinal));
        var detailRequests = requests.Count(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith(DiscrepancyId, StringComparison.Ordinal));

        panel.Find(".discrepancy-reload").Click();

        panel.WaitForAssertion(() => Assert.Equal(
            detailRequests + 1,
            requests.Count(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith(DiscrepancyId, StringComparison.Ordinal))));
        Assert.Empty(panel.FindAll(".discrepancy-action-error"));
    }

    [Fact]
    public void Failed_details_can_be_retried()
    {
        detailsStatus = HttpStatusCode.ServiceUnavailable;
        detailsResponse = """{"title":"Cargo service unavailable"}""";
        var panel = RenderPanel();

        panel.Find(".discrepancy-open").Click();

        panel.WaitForAssertion(() => Assert.Contains("Cargo service unavailable", panel.Find(".discrepancy-modal .api-error-state").TextContent, StringComparison.Ordinal));
        Assert.Empty(panel.FindAll(".discrepancy-description"));

        detailsStatus = HttpStatusCode.OK;
        detailsResponse = ResolvableDetails;
        panel.Find(".discrepancy-modal .api-error-state button").Click();

        panel.WaitForAssertion(() => Assert.Contains("Two pallets missing", panel.Find(".discrepancy-description").TextContent, StringComparison.Ordinal));
        Assert.Empty(panel.FindAll(".discrepancy-modal .api-error-state"));
    }

    [Fact]
    public void A_resolution_that_failed_on_an_unavailable_api_keeps_its_values_and_can_be_sent_again()
    {
        detailsResponse = ResolvableDetails;
        actionStatus = HttpStatusCode.ServiceUnavailable;
        actionResponse = """{"title":"Cargo service unavailable"}""";
        var panel = RenderPanel();

        panel.Find(".discrepancy-open").Click();
        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".discrepancy-resolve-submit")));
        panel.Find("#discrepancy-disposition").Change("Corrected");
        panel.Find("#discrepancy-summary").Change("Pallets found at the neighbouring berth.");
        panel.Find(".discrepancy-resolve-submit").Click();
        panel.Find(".discrepancy-confirm-yes").Click();

        panel.WaitForAssertion(() => Assert.Contains("Cargo service unavailable", panel.Find(".discrepancy-resolve .discrepancy-action-error").TextContent, StringComparison.Ordinal));
        Assert.Empty(panel.FindAll(".discrepancy-reload"));
        Assert.Equal("Corrected", panel.Find("#discrepancy-disposition").GetAttribute("value"));
        Assert.Equal("Pallets found at the neighbouring berth.", panel.Find("#discrepancy-summary").GetAttribute("value"));

        actionStatus = HttpStatusCode.OK;
        actionResponse = ResolvableDetails;
        panel.Find(".discrepancy-resolve-submit").Click();
        panel.Find(".discrepancy-confirm-yes").Click();

        panel.WaitForAssertion(() =>
        {
            var resolutions = requests
                .Select((request, index) => (request, index))
                .Where(entry => entry.request.RequestUri!.AbsolutePath.EndsWith("/resolution", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(2, resolutions.Count);
            Assert.Equal(requestBodies[resolutions[0].index], requestBodies[resolutions[1].index]);
            Assert.Empty(panel.FindAll(".discrepancy-action-error"));
        });
    }

    [Fact]
    public void Confirmations_and_errors_stay_next_to_the_action_that_raised_them()
    {
        detailsResponse = ResolvableDetails;
        var panel = RenderPanel();

        panel.Find(".discrepancy-open").Click();
        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".discrepancy-resolve-submit")));

        panel.Find(".discrepancy-void-submit").Click();

        Assert.NotNull(panel.Find(".discrepancy-void .discrepancy-action-error"));
        Assert.Empty(panel.FindAll(".discrepancy-resolve .discrepancy-action-error"));

        panel.Find("#discrepancy-disposition").Change("NoDiscrepancyFound");
        panel.Find("#discrepancy-summary").Change("Pallets found at the neighbouring berth.");
        panel.Find(".discrepancy-resolve-submit").Click();

        Assert.NotNull(panel.Find(".discrepancy-resolve .discrepancy-confirm"));
        Assert.Empty(panel.FindAll(".discrepancy-void .discrepancy-action-error"));
        Assert.NotNull(panel.Find(".discrepancy-void"));
        Assert.NotNull(panel.Find(".discrepancy-step-note"));
        Assert.True(panel.Find(".discrepancy-void-submit").HasAttribute("disabled"));

        panel.Find(".discrepancy-confirm-no").Click();

        Assert.Empty(panel.FindAll(".discrepancy-confirm"));
        Assert.False(panel.Find(".discrepancy-resolve-submit").HasAttribute("disabled"));
    }

    [Fact]
    public void Voiding_requires_a_reason()
    {
        detailsResponse = ResolvableDetails;
        var panel = RenderPanel();

        panel.Find(".discrepancy-open").Click();
        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".discrepancy-void-submit")));
        panel.Find(".discrepancy-void-submit").Click();

        Assert.Contains("Give a reason", panel.Find(".discrepancy-action-error").TextContent, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".discrepancy-confirm"));
    }

    [Fact]
    public void Registering_resolves_the_container_number_and_opens_the_new_discrepancy()
    {
        actionStatus = HttpStatusCode.Created;
        var changed = 0;
        var panel = RenderPanel(onChanged: () => changed++);

        panel.Find(".discrepancy-register-toggle").Click();
        panel.Find("#discrepancy-type").Change("SealMismatch");
        panel.Find("#discrepancy-severity").Change("High");
        panel.Find("#discrepancy-container").Input("msku1234567");
        panel.Find("#discrepancy-observed").Change("2026-09-29T07:30:00");
        panel.Find("#discrepancy-description").Change("Seal differs from the booking");
        panel.Find(".discrepancy-form").Submit();

        panel.WaitForAssertion(() =>
        {
            var index = requests.FindIndex(request => request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/discrepancies", StringComparison.Ordinal));
            Assert.True(index >= 0);
            Assert.Contains($"\"containerId\":\"{ContainerId}\"", requestBodies[index], StringComparison.Ordinal);
            Assert.Contains("\"type\":\"SealMismatch\"", requestBodies[index], StringComparison.Ordinal);
            Assert.Contains("\"observedAtUtc\":\"2026-09-29T07:30:00Z\"", requestBodies[index], StringComparison.Ordinal);
        });
        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".discrepancy-modal")));
        Assert.Equal(1, changed);
        Assert.Contains(requests, request => request.RequestUri!.Query.Contains("search=MSKU1234567", StringComparison.Ordinal));
    }

    [Fact]
    public void Registration_is_validated_before_anything_is_sent()
    {
        var panel = RenderPanel();

        panel.Find(".discrepancy-register-toggle").Click();
        panel.Find("#discrepancy-type").Change("TemperatureRelated");
        panel.Find(".discrepancy-form").Submit();

        Assert.Equal(3, panel.FindAll(".discrepancy-field-error").Count);
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void The_observation_time_cannot_be_picked_in_the_future()
    {
        var panel = RenderPanel();

        panel.Find(".discrepancy-register-toggle").Click();

        var input = panel.Find("#discrepancy-observed");
        var max = DateTime.ParseExact(input.GetAttribute("max")!, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        var value = DateTime.Parse(input.GetAttribute("value")!, CultureInfo.InvariantCulture);
        Assert.InRange(max, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow);
        Assert.True(value <= max);
    }

    [Fact]
    public void An_unknown_container_number_is_rejected_without_registering()
    {
        containersResponse = NoContainers;
        var panel = RenderPanel();

        panel.Find(".discrepancy-register-toggle").Click();
        panel.Find("#discrepancy-type").Change("Damage");
        panel.Find("#discrepancy-severity").Change("Low");
        panel.Find("#discrepancy-container").Input("TGHU0000001");
        panel.Find("#discrepancy-description").Change("Dent");
        panel.Find(".discrepancy-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains("No container TGHU0000001", panel.Find(".discrepancy-field-error").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void Server_validation_errors_are_shown_next_to_the_fields()
    {
        actionStatus = HttpStatusCode.BadRequest;
        actionResponse = """{"title":"One or more validation errors occurred.","errors":{"observedAtUtc":["An observation time must not be in the future."]}}""";
        var panel = RenderPanel();

        panel.Find(".discrepancy-register-toggle").Click();
        panel.Find("#discrepancy-type").Change("Shortage");
        panel.Find("#discrepancy-severity").Change("Low");
        panel.Find("#discrepancy-description").Change("Two pallets missing");
        panel.Find(".discrepancy-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains("must not be in the future", panel.Find(".discrepancy-field-error").TextContent, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Completed", "is completed")]
    [InlineData("Planned", "once the cargo is loaded")]
    public void Registration_is_not_offered_outside_the_cargo_movement(string bookingStatus, string explanation)
    {
        var panel = RenderPanel(bookingStatus: bookingStatus);

        Assert.Empty(panel.FindAll(".discrepancy-register-toggle"));
        Assert.Contains(explanation, panel.Find(".discrepancy-closed-note").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenDiscrepancyAsync_shows_a_discrepancy_on_request()
    {
        var panel = RenderPanel();

        await panel.InvokeAsync(() => panel.Instance.OpenDiscrepancyAsync(Guid.Parse(DiscrepancyId)));

        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".discrepancy-modal")));
    }

    private IRenderedComponent<CargoDiscrepanciesPanel> RenderPanel(
        string bookingStatus = "Unloaded",
        Action<(Guid ContainerId, string? ContainerNumber)>? onOpenContainer = null,
        Action? onChanged = null,
        params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, _) =>
        {
            requests.Add(request);
            requestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(CancellationToken.None));

            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/containers", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, containersResponse);
            }

            if (request.Method == HttpMethod.Post)
            {
                return Json(actionStatus, actionResponse);
            }

            if (path.EndsWith("/discrepancies", StringComparison.Ordinal))
            {
                return Json(listStatus, listResponse);
            }

            return Json(detailsStatus, detailsResponse);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CargoBookingsApiClient(httpClient));
        ConfigureAuthorization(permissions.Length == 0
            ? [CurrentUserAuthorizationState.CargoManagePermission]
            : permissions);

        var panel = Render<CargoDiscrepanciesPanel>(parameters => parameters
            .Add(component => component.BookingId, Guid.Parse(BookingId))
            .Add(component => component.BookingStatus, bookingStatus)
            .Add(component => component.OnOpenContainer, EventCallback.Factory.Create<(Guid ContainerId, string? ContainerNumber)>(this, container => onOpenContainer?.Invoke(container)))
            .Add(component => component.OnChanged, EventCallback.Factory.Create(this, () => onChanged?.Invoke())));

        panel.WaitForAssertion(() => Assert.NotEmpty(requests));

        return panel;
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
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
                        .Select(permission => new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission))
                        .ToArray(),
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();
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
