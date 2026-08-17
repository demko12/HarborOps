using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Components.Pages.Fleet;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class FleetPageTests : BunitContext
{
    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, Page(Row()));

    [Fact]
    public void Vessels_render_with_their_identity_and_type()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row(name: "Northern Star", imoNumber: "5100001")));

        var page = RenderFleetPage();

        var row = Assert.Single(page.FindAll(".fleet-vessel-row"));

        Assert.Contains("Northern Star", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("IMO 5100001", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Bulk carrier", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Montenegro", row.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_badge_carries_the_status_wording_and_its_modifier()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row(currentStatus: "At Port")));

        var page = RenderFleetPage();

        var badge = page.Find(".vessel-status");

        Assert.Equal("At Port", badge.TextContent.Trim());
        Assert.Contains("vessel-status-port", badge.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void A_vessel_without_deadweight_renders_a_placeholder()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row(dwt: "null")));

        var page = RenderFleetPage();

        Assert.Contains("—", page.Find(".fleet-vessel-row").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unassigned_vessel_is_named_rather_than_left_blank()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row(fleetName: "null")));

        var page = RenderFleetPage();

        Assert.Contains("Unassigned", page.Find(".vessel-fleet").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_cards_describe_the_loaded_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(
            Row(name: "Alpha", imoNumber: "5100001", currentStatus: "Active", dwt: "1000"),
            Row(name: "Bravo", imoNumber: "5100002", currentStatus: "At Port", dwt: "2000"),
            Row(name: "Charlie", imoNumber: "5100003", currentStatus: "Under Maintenance", dwt: "3000")));

        var page = RenderFleetPage();

        var values = page.FindAll(".fleet-summary-card strong");

        Assert.Equal("2", values[0].TextContent.Trim());
        Assert.Equal("0", values[1].TextContent.Trim());
        Assert.Equal("1", values[2].TextContent.Trim());
        Assert.Equal("1", values[3].TextContent.Trim());
        Assert.Equal("6K", values[5].TextContent.Trim());
    }

    [Fact]
    public void An_empty_result_offers_the_empty_state_instead_of_a_table()
    {
        respond = _ => Json(HttpStatusCode.OK, Page());

        var page = RenderFleetPage();

        Assert.Empty(page.FindAll(".fleet-vessel-row"));
        Assert.Contains("No vessels found", page.Find(".empty-state").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_api_failure_reports_the_problem_from_the_response()
    {
        respond = _ => Json(
            HttpStatusCode.InternalServerError,
            """{"title":"Unable to load vessels","detail":"The register is offline.","status":500}""");

        var page = RenderFleetPage();

        var error = page.Find(".api-error-state");

        Assert.Contains("Unable to load vessels", error.TextContent, StringComparison.Ordinal);
        Assert.Contains("The register is offline.", error.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_forbidden_response_explains_the_missing_permission()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, "{}");

        var page = RenderFleetPage();

        Assert.Contains(
            "Your account does not have permission to view vessels.",
            page.Find(".api-error-state").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Retrying_after_a_failure_loads_the_register_again()
    {
        respond = _ => Json(HttpStatusCode.InternalServerError, "{}");

        var page = RenderFleetPage();
        var requestsBeforeRetry = requestedUris.Count;

        respond = _ => Json(HttpStatusCode.OK, Page(Row(name: "Northern Star")));

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.True(requestedUris.Count > requestsBeforeRetry);
            Assert.Contains("Northern Star", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Searching_sends_the_term_to_the_api()
    {
        var page = RenderFleetPage();

        page.Find(".fleet-input-shell input").Input("Northern");

        page.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.Query.Contains("search=Northern", StringComparison.Ordinal)));
    }

    [Fact]
    public void Filtering_by_status_sends_the_status_to_the_api()
    {
        var page = RenderFleetPage();

        page.Find(".fleet-filters select").Change("At Port");

        page.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.Query.Contains("status=At%20Port", StringComparison.Ordinal)));
    }

    [Fact]
    public void Changing_a_filter_returns_to_the_first_page()
    {
        respond = request => Json(HttpStatusCode.OK, Page(
            request.RequestUri!.Query.Contains("pageNumber=2", StringComparison.Ordinal) ? 2 : 1,
            60,
            Row()));

        var page = RenderFleetPage();

        page.FindAll(".fleet-pagination button")[1].Click();
        page.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.Query.Contains("pageNumber=2", StringComparison.Ordinal)));

        page.Find(".fleet-input-shell input").Input("Northern");

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris[^1].Query;

            Assert.Contains("search=Northern", lastQuery, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Resetting_clears_the_search_and_the_status()
    {
        var page = RenderFleetPage();

        page.Find(".fleet-input-shell input").Input("Northern");
        page.Find(".fleet-filters select").Change("At Port");
        page.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.Query.Contains("status=At%20Port", StringComparison.Ordinal)));

        page.Find(".fleet-reset-button").Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris[^1].Query;

            Assert.Contains("search=&", lastQuery, StringComparison.Ordinal);
            Assert.Contains("status=&", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Paging_forward_requests_the_next_page()
    {
        respond = request => Json(HttpStatusCode.OK, Page(
            request.RequestUri!.Query.Contains("pageNumber=2", StringComparison.Ordinal) ? 2 : 1,
            60,
            Row()));

        var page = RenderFleetPage();

        page.FindAll(".fleet-pagination button")[1].Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                requestedUris,
                uri => uri.Query.Contains("pageNumber=2", StringComparison.Ordinal));
            Assert.Contains("Page 2 of 3", page.Find(".fleet-page-indicator").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Paging_back_from_the_first_page_is_not_offered()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(1, 60, Row()));

        var page = RenderFleetPage();

        Assert.True(page.FindAll(".fleet-pagination button")[0].HasAttribute("disabled"));
    }

    [Fact]
    public void The_print_action_is_offered_in_the_page_header()
    {
        var page = RenderFleetPage();

        var print = page.Find(".fleet-header .print-button");

        Assert.Equal("Print the vessel register", print.GetAttribute("aria-label"));
        Assert.NotNull(print.QuerySelector(".bi-printer"));
    }

    [Fact]
    public void The_print_action_is_itself_left_out_of_the_printed_output()
    {
        var page = RenderFleetPage();

        Assert.Contains("no-print", page.Find(".print-button").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void The_print_action_asks_the_browser_to_print()
    {
        var invocation = JSInterop.SetupVoid("print");

        var page = RenderFleetPage();

        page.Find(".print-button").Click();

        invocation.SetVoidResult();

        Assert.Single(JSInterop.Invocations["print"]);
    }

    [Fact]
    public void The_printed_summary_repeats_the_applied_filters()
    {
        var page = RenderFleetPage();

        page.Find(".fleet-input-shell input").Input("Northern");
        page.Find(".fleet-filters select").Change("At Port");

        page.WaitForAssertion(() =>
        {
            var summary = page.Find(".fleet-print-summary").TextContent;

            Assert.Contains("Northern", summary, StringComparison.Ordinal);
            Assert.Contains("At Port", summary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_printed_summary_says_so_when_no_filters_are_applied()
    {
        var page = RenderFleetPage();

        var summary = page.Find(".fleet-print-summary").TextContent;

        Assert.Contains("All vessels", summary, StringComparison.Ordinal);
        Assert.Contains("All statuses", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_printed_summary_reports_the_page_and_the_row_range()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(1, 60, Row()));

        var page = RenderFleetPage();

        var summary = page.Find(".fleet-print-summary").TextContent;

        Assert.Contains("1 of 3", summary, StringComparison.Ordinal);
        Assert.Contains("of 60", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_printed_summary_is_hidden_from_the_screen_layout()
    {
        var page = RenderFleetPage();

        Assert.Contains("print-only", page.Find(".fleet-print-summary").ClassName, StringComparison.Ordinal);
    }

    private IRenderedComponent<Fleet> RenderFleetPage()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new FleetApiClient(httpClient));

        var page = Render<Fleet>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private static string Row(
        string name = "Northern Star",
        string imoNumber = "5100001",
        string currentStatus = "Active",
        string dwt = "52000",
        string fleetName = "\"Adriatic Fleet\"")
    {
        return $$"""
            {"vesselId":"40000000-0000-0000-0000-000000000009","name":"{{name}}",
            "imoNumber":"{{imoNumber}}","flagState":"Montenegro","currentStatus":"{{currentStatus}}",
            "vesselTypeId":"50000000-0000-0000-0000-000000000001","vesselTypeName":"Bulk carrier",
            "companyId":"60000000-0000-0000-0000-000000000001","dwt":{{dwt}},
            "updatedAtUtc":"2026-08-01T10:00:00Z","lifecycleStage":null,
            "fleetId":"70000000-0000-0000-0000-000000000001","fleetName":{{fleetName}}}
            """;
    }

    private static string Page(params string[] rows) => Page(1, rows.Length, rows);

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
