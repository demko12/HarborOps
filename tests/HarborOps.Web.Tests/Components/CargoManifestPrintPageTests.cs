using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using HarborOps.Web.Api.Cargo;
using HarborOps.Web.Components.Pages.Cargo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class CargoManifestPrintPageTests : BunitContext
{
    private const string BookingId = "20000000-0000-0000-0000-000000000001";
    private const string VesselId = "30000000-0000-0000-0000-000000000003";
    private const string ScriptPayload = "<script>alert('x')</script>";

    private readonly List<HttpRequestMessage> requests = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, FilteredManifest());

    public CargoManifestPrintPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_page_requests_the_print_model_with_the_applied_filters_only()
    {
        RenderPage("?search=msku&status=Loaded&containerType=42r1&portOfLoadingCode=nlrtm&portOfDischargeCode=USNYC&returnPage=3&from=status%3DInTransit");

        var uri = requests[0].RequestUri!;
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal($"/api/cargo/bookings/{BookingId}/containers/print", uri.AbsolutePath);
        Assert.Equal("MSKU", query["search"].ToString());
        Assert.Equal("Loaded", query["status"].ToString());
        Assert.Equal("42R1", query["containerType"].ToString());
        Assert.Equal("NLRTM", query["portOfLoadingCode"].ToString());
        Assert.Equal("USNYC", query["portOfDischargeCode"].ToString());
        Assert.False(query.ContainsKey("returnPage"));
        Assert.False(query.ContainsKey("pageNumber"));
        Assert.False(query.ContainsKey("from"));
    }

    [Fact]
    public void A_filtered_manifest_shows_its_filters_generation_time_counts_and_weights()
    {
        var page = RenderPage("?status=Loaded");

        Assert.Equal("BK-2026-0145", page.Find(".manifest-print-number").TextContent);
        Assert.Equal("Filtered manifest", page.Find(".manifest-print-scope").TextContent.Trim());
        Assert.Equal("2026-09-28 14:32 UTC", page.Find(".manifest-print-generated").TextContent.Trim());
        Assert.Equal("4 containers of 52 on this booking", page.Find(".manifest-print-count").TextContent.Trim());
        Assert.Equal(["StatusLoaded"], page.FindAll(".manifest-print-filters li").Select(item => item.TextContent.Replace(" ", string.Empty, StringComparison.Ordinal)));
        Assert.Equal("37,500.000 kg across 3 containers", page.Find(".manifest-print-weight").TextContent.Trim());
        Assert.Equal("1 container with unknown weight, not included in the total", page.Find(".manifest-print-unknown").TextContent.Trim());
    }

    [Fact]
    public void Rows_are_listed_in_the_returned_order_and_legacy_values_stay_blank()
    {
        var page = RenderPage("?status=Loaded");

        var rows = page.FindAll(".manifest-print-row");

        Assert.Equal(
            ["MSKU0000001", "MSKU0000002", "MSKU0000003", "TGHU0000001"],
            rows.Select(row => row.QuerySelector(".manifest-print-container")!.TextContent));

        var first = rows[0].QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.Equal(["MSKU0000001", "42R1", "NLRTM", "USNYC", "12,500.000", "Loaded", VesselId], first);

        var legacy = rows[3].QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.Equal(["TGHU0000001", "20GP", string.Empty, string.Empty, string.Empty, "Loaded", string.Empty], legacy);
    }

    [Fact]
    public void A_complete_manifest_is_distinguished_from_a_filtered_one()
    {
        respond = _ => Json(HttpStatusCode.OK, Manifest(isFiltered: false, matching: 2, total: 2, filters: NoFilters, rows: [Row("MSKU0000001", weight: "100")], knownWeight: "100", knownCount: 1, unknownCount: 0));

        var page = RenderPage(string.Empty);

        Assert.Equal("Complete manifest", page.Find(".manifest-print-scope").TextContent.Trim());
        Assert.Equal("All 2 containers on this booking", page.Find(".manifest-print-count").TextContent.Trim());
        Assert.Equal("None, every container on the booking", page.Find(".manifest-print-filters").TextContent.Trim());
        Assert.Equal("None", page.Find(".manifest-print-unknown").TextContent.Trim());
    }

    [Fact]
    public void A_filter_without_matches_shows_an_explicit_message_instead_of_a_table()
    {
        respond = _ => Json(HttpStatusCode.OK, Manifest(isFiltered: true, matching: 0, total: 52, filters: """{"search":"ZZZZ","status":null,"containerType":null,"portOfLoadingCode":null,"portOfDischargeCode":null}""", rows: [], knownWeight: "0", knownCount: 0, unknownCount: 0));

        var page = RenderPage("?search=ZZZZ");

        Assert.Equal("No containers on this booking match the applied filters.", page.Find(".manifest-print-empty").TextContent.Trim());
        Assert.Empty(page.FindAll(".manifest-print-table"));
        Assert.Equal("0 containers of 52 on this booking", page.Find(".manifest-print-count").TextContent.Trim());
        Assert.Equal("No known weights", page.Find(".manifest-print-weight").TextContent.Trim());
    }

    [Fact]
    public void Container_values_are_html_encoded_and_unicode_is_kept()
    {
        respond = _ => Json(HttpStatusCode.OK, Manifest(isFiltered: false, matching: 1, total: 1, filters: NoFilters, rows: [Row("MSKU0000001", containerType: ScriptPayload, status: "Hladnjača ❄")], knownWeight: "12500", knownCount: 1, unknownCount: 0, bookingNumber: "BK-ČŽ-Đš"));

        var page = RenderPage(string.Empty);

        Assert.DoesNotContain("<script>", page.Markup, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", page.Markup, StringComparison.Ordinal);
        Assert.Equal(ScriptPayload, page.Find(".manifest-print-row td:nth-child(2)").TextContent);
        Assert.Equal("Hladnjača ❄", page.Find(".manifest-print-row td:nth-child(6)").TextContent);
        Assert.Equal("BK-ČŽ-Đš", page.Find(".manifest-print-number").TextContent);
    }

    [Fact]
    public void The_page_reports_that_it_is_preparing_the_manifest()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        respond = async _ =>
        {
            await release.Task;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(FilteredManifest(), Encoding.UTF8, "application/json"),
            };
        };

        var page = RenderPage("?status=Loaded");

        Assert.Equal("Preparing the printable manifest...", page.Find(".manifest-print-note").TextContent.Trim());
        Assert.True(page.Find(".manifest-print-button").HasAttribute("disabled"));

        release.SetResult();

        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".manifest-print-row").Count), TimeSpan.FromSeconds(5));
        Assert.False(page.Find(".manifest-print-button").HasAttribute("disabled"));
        Assert.Single(requests);
    }

    [Fact]
    public void A_failed_load_can_be_retried()
    {
        respond = _ => Json(HttpStatusCode.ServiceUnavailable, """{"title":"Unable to prepare the printable manifest","status":503}""");

        var page = RenderPage("?status=Loaded");

        Assert.Contains("Unable to prepare the printable manifest", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".manifest-print-sheet"));

        respond = _ => Json(HttpStatusCode.OK, FilteredManifest());
        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".manifest-print-row").Count));
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public void Returning_to_the_same_filters_does_not_request_the_manifest_again()
    {
        var page = RenderPage("?status=Loaded");

        page.Render();

        Assert.Single(requests);
    }

    [Fact]
    public void Too_many_matches_explains_how_to_narrow_the_filters()
    {
        respond = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"limit":["More than 10,000 containers match. Narrow the filters to export or print at most 10,000 containers."]}}""");

        var page = RenderPage("?search=MSKU&returnPage=2");

        Assert.Equal(
            "More than 10,000 containers match. Narrow the filters to export or print at most 10,000 containers.",
            page.Find(".manifest-print-limit p").TextContent);
        Assert.Equal($"/cargo/{BookingId}?manifestSearch=MSKU&manifestPage=2&tab=manifest", page.Find(".manifest-print-limit a").GetAttribute("href"));
        Assert.Empty(page.FindAll(".manifest-print-sheet"));
    }

    [Fact]
    public void A_forbidden_manifest_explains_the_missing_permission()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, "{}");

        var page = RenderPage(string.Empty);

        Assert.Contains("You cannot print this manifest", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".manifest-print-sheet"));
    }

    [Fact]
    public void An_unknown_booking_is_reported_as_gone()
    {
        respond = _ => Json(HttpStatusCode.NotFound, """{"title":"Cargo booking not found.","status":404}""");

        var page = RenderPage(string.Empty);

        Assert.Contains("Booking not found", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Back_to_booking_returns_to_the_same_filters_page_and_board()
    {
        var page = RenderPage("?search=msku&status=Loaded&returnPage=3&from=status%3DInTransit");

        Assert.Equal(
            $"/cargo/{BookingId}?from=status%3DInTransit&manifestSearch=MSKU&manifestStatus=Loaded&manifestPage=3&tab=manifest",
            page.Find(".manifest-print-back").GetAttribute("href"));
    }

    [Fact]
    public void Back_to_booking_without_filters_returns_to_the_first_page()
    {
        var page = RenderPage(string.Empty);

        Assert.Equal($"/cargo/{BookingId}?tab=manifest", page.Find(".manifest-print-back").GetAttribute("href"));
    }

    [Fact]
    public void Print_uses_the_browser_print_dialog()
    {
        var page = RenderPage("?status=Loaded");

        page.Find(".manifest-print-button").Click();

        Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == "print");
    }

    [Fact]
    public void The_print_route_requires_cargo_access_even_on_direct_navigation()
    {
        var authorize = typeof(CargoManifestPrint).GetCustomAttribute<AuthorizeAttribute>();
        var route = typeof(CargoManifestPrint).GetCustomAttribute<RouteAttribute>();

        Assert.Equal("AccessCargo", authorize?.Policy);
        Assert.Equal("/cargo/{BookingId:guid}/manifest/print", route?.Template);
    }

    private const string NoFilters = """{"search":null,"status":null,"containerType":null,"portOfLoadingCode":null,"portOfDischargeCode":null}""";

    private static string FilteredManifest()
    {
        return Manifest(
            isFiltered: true,
            matching: 4,
            total: 52,
            filters: """{"search":null,"status":"Loaded","containerType":null,"portOfLoadingCode":null,"portOfDischargeCode":null}""",
            rows:
            [
                Row("MSKU0000001", vesselId: VesselId),
                Row("MSKU0000002"),
                Row("MSKU0000003"),
                Row("TGHU0000001", containerType: "20GP", portOfLoading: null, portOfDischarge: null, weight: null),
            ],
            knownWeight: "37500",
            knownCount: 3,
            unknownCount: 1);
    }

    private static string Manifest(
        bool isFiltered,
        int matching,
        int total,
        string filters,
        string[] rows,
        string knownWeight,
        int knownCount,
        int unknownCount,
        string bookingNumber = "BK-2026-0145")
    {
        return $$"""
            {"cargoBookingId":"{{BookingId}}","bookingNumber":"{{bookingNumber}}","generatedAtUtc":"2026-09-28T14:32:05Z","filters":{{filters}},"isFiltered":{{(isFiltered ? "true" : "false")}},"matchingCount":{{matching}},"bookingContainerCount":{{total}},"knownWeightKg":{{knownWeight}},"knownWeightCount":{{knownCount}},"unknownWeightCount":{{unknownCount}},"rowLimit":10000,"rows":[{{string.Join(',', rows)}}]}
            """;
    }

    private static string Row(
        string containerNumber,
        string containerType = "42R1",
        string status = "Loaded",
        string? portOfLoading = "NLRTM",
        string? portOfDischarge = "USNYC",
        string? weight = "12500",
        string? vesselId = null)
    {
        static string JsonString(string? value) =>
            value is null ? "null" : System.Text.Json.JsonSerializer.Serialize(value);

        return $$"""
            {"containerId":"{{Guid.NewGuid()}}","containerNumber":"{{containerNumber}}","containerType":{{JsonString(containerType)}},"status":{{JsonString(status)}},"portOfLoadingCode":{{JsonString(portOfLoading)}},"portOfDischargeCode":{{JsonString(portOfDischarge)}},"weightKg":{{weight ?? "null"}},"vesselId":{{JsonString(vesselId)}}}
            """;
    }

    private IRenderedComponent<CargoManifestPrint> RenderPage(string query)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add(request);

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CargoBookingsApiClient(httpClient));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/cargo/{BookingId}/manifest/print{query}");

        var page = Render<CargoManifestPrint>(parameters => parameters
            .Add(component => component.BookingId, Guid.Parse(BookingId)));
        page.WaitForAssertion(() => Assert.NotEmpty(requests));

        return page;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return responseFactory(request);
        }
    }
}
