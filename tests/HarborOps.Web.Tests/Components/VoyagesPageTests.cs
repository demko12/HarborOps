using System.Globalization;
using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Voyages;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class VoyagesPageTests : BunitContext
{
    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, Page());

    [Fact]
    public void Rows_render_the_route_and_utc_stamps()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0001")));

        var page = RenderVoyagesPage();

        Assert.Equal("MEBAR → ITGOA", page.Find("td.voyage-route").TextContent.Trim());
        Assert.Contains("15 Sep 2026 08:00", page.Markup, StringComparison.Ordinal);
        Assert.Contains("18 Sep 2026 17:30", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_actual_dates_render_as_a_placeholder_instead_of_breaking_the_row()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0002")));

        var page = RenderVoyagesPage();

        var actualCell = page.FindAll("td.voyage-stamps")[1];

        Assert.Equal(2, actualCell.QuerySelectorAll("span.missing").Length);
        Assert.Contains(Voyages.MissingValue, actualCell.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Departures_inside_the_next_day_are_marked_on_the_row()
    {
        var imminent = DateTime.UtcNow.AddHours(2);

        respond = _ => Json(HttpStatusCode.OK, Page(
            Row("VOY-0003", plannedDeparture: Stamp(imminent)),
            Row("VOY-0004")));

        var page = RenderVoyagesPage();

        var markedRow = Assert.Single(page.FindAll("tbody tr.imminent"));

        Assert.Contains("VOY-0003", markedRow.TextContent, StringComparison.Ordinal);
        Assert.Equal("Departs within 24 hours", markedRow.GetAttribute("title"));
    }

    [Fact]
    public void The_status_column_shows_the_position_in_the_voyage_lifecycle()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row("VOY-0005", status: "InProgress")));

        var page = RenderVoyagesPage();

        Assert.Equal(3, page.FindAll(".voyage-steps i").Count);
        Assert.Equal(2, page.FindAll(".voyage-steps i.on").Count);
        Assert.Equal("InProgress", page.Find(".voyage-status").TextContent.Trim());
    }

    [Fact]
    public void Summary_cards_show_a_count_for_each_tracked_status()
    {
        respond = request =>
        {
            var query = request.RequestUri!.Query;
            var total = 22;

            if (query.Contains("status=Planned", StringComparison.Ordinal))
            {
                total = 7;
            }
            else if (query.Contains("status=InProgress", StringComparison.Ordinal))
            {
                total = 3;
            }
            else if (query.Contains("status=Completed", StringComparison.Ordinal))
            {
                total = 12;
            }

            return Json(HttpStatusCode.OK, Page(1, total, Row("VOY-0100")));
        };

        var page = RenderVoyagesPage();

        page.WaitForAssertion(() =>
        {
            var values = page.FindAll(".voyages-kpi .voyages-kpi-value");

            Assert.Equal(4, values.Count);
            Assert.Equal("7", values[0].TextContent.Trim());
            Assert.Equal("3", values[1].TextContent.Trim());
            Assert.Equal("12", values[2].TextContent.Trim());
            Assert.Equal(Voyages.MissingValue, values[3].TextContent.Trim());
        });
    }

    [Fact]
    public void Applying_filters_sends_the_window_status_and_page_size_to_the_api()
    {
        var page = RenderVoyagesPage();

        page.Find("#voyages-from").Change("2026-09-01");
        page.Find("#voyages-to").Change("2026-09-30");
        page.Find("#voyages-status").Change("Planned");
        page.Find("#voyages-page-size").Change("50");

        page.WaitForAssertion(() => Assert.Contains(
            ResultQueries(),
            query => query.Contains("from=2026-09-01", StringComparison.Ordinal)
                && query.Contains("to=2026-09-30", StringComparison.Ordinal)
                && query.Contains("status=Planned", StringComparison.Ordinal)
                && query.Contains("pageSize=50", StringComparison.Ordinal)));
    }

    [Fact]
    public void Searching_sends_the_voyage_number_to_the_api()
    {
        var page = RenderVoyagesPage();

        page.Find("#voyages-search").Input("VOY-06");

        page.WaitForAssertion(() => Assert.Contains(
            ResultQueries(),
            query => query.Contains("search=VOY-06", StringComparison.Ordinal)));
    }

    [Fact]
    public void Resetting_the_filters_returns_to_the_default_window_and_first_page()
    {
        var page = RenderVoyagesPage();

        page.Find("#voyages-status").Change("Completed");
        page.WaitForAssertion(() => Assert.Contains(
            ResultQueries(),
            query => query.Contains("status=Completed", StringComparison.Ordinal)));

        page.Find(".voyages-reset").Click();

        var defaultFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        var defaultTo = defaultFrom.AddDays(Voyages.DefaultWindowDays);

        page.WaitForAssertion(() =>
        {
            var lastQuery = ResultQueries()[^1];

            Assert.Contains("status=&", lastQuery, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
            Assert.Contains($"from={defaultFrom:yyyy-MM-dd}", lastQuery, StringComparison.Ordinal);
            Assert.Contains($"to={defaultTo:yyyy-MM-dd}", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Empty_data_shows_the_empty_state_instead_of_a_table()
    {
        respond = _ => Json(HttpStatusCode.OK, Page());

        var page = RenderVoyagesPage();

        Assert.Empty(page.FindAll("table.voyages-table"));
        Assert.Contains("No voyages in this window", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreachable_api_shows_the_error_state_with_a_retry_action()
    {
        respond = _ => throw new HttpRequestException("Connection failed.");

        var page = RenderVoyagesPage();

        Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("table.voyages-table"));
        Assert.NotNull(page.Find(".api-error-state button"));
    }

    [Fact]
    public void Retry_reloads_the_voyages_after_a_failure()
    {
        var attempt = 0;
        respond = _ =>
        {
            attempt++;

            return attempt == 1
                ? throw new HttpRequestException("Connection failed.")
                : Json(HttpStatusCode.OK, Page(Row("VOY-0007")));
        };

        var page = RenderVoyagesPage();

        Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal);

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("VOY-0007", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void A_forbidden_response_explains_the_missing_permission()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403}""");

        var page = RenderVoyagesPage();

        Assert.Contains(Voyages.ForbiddenTitle, page.Markup, StringComparison.Ordinal);
        Assert.Contains(Voyages.ForbiddenMessage, page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_problems_are_listed_above_the_results()
    {
        respond = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"to":["To date must not be earlier than the from date."]}}""");

        var page = RenderVoyagesPage();

        Assert.Contains(
            "To date must not be earlier than the from date.",
            page.Find(".voyages-validation").TextContent,
            StringComparison.Ordinal);
        Assert.Empty(page.FindAll("table.voyages-table"));
    }

    [Fact]
    public void Paging_requests_the_next_page_and_disables_previous_on_the_first_page()
    {
        respond = request =>
        {
            var pageNumber = request.RequestUri!.Query.Contains("pageNumber=2", StringComparison.Ordinal) ? 2 : 1;

            return Json(HttpStatusCode.OK, Page(pageNumber, 60, Row($"VOY-PAGE-{pageNumber}")));
        };

        var page = RenderVoyagesPage();

        var previous = page.FindAll(".voyages-pagination button")[0];

        Assert.True(previous.HasAttribute("disabled"));
        Assert.Contains("Page 1 of 3", page.Markup, StringComparison.Ordinal);

        page.FindAll(".voyages-pagination button")[1].Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("VOY-PAGE-2", page.Markup, StringComparison.Ordinal);
            Assert.Contains(
                ResultQueries(),
                query => query.Contains("pageNumber=2", StringComparison.Ordinal));
            Assert.False(page.FindAll(".voyages-pagination button")[0].HasAttribute("disabled"));
        });
    }

    /// the board also asks for the planned count with pageSize=1; those are not result requests
    private List<string> ResultQueries()
    {
        return requestedUris
            .Select(uri => uri.Query)
            .Where(query => !query.Contains("pageSize=1&", StringComparison.Ordinal)
                && !query.EndsWith("pageSize=1", StringComparison.Ordinal))
            .ToList();
    }

    private IRenderedComponent<Voyages> RenderVoyagesPage()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new VoyagesApiClient(httpClient));

        var page = Render<Voyages>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private static string Stamp(DateTime value)
    {
        return value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string Row(
        string voyageNumber,
        string plannedDeparture = "2026-09-15T08:00:00Z",
        string plannedArrival = "2026-09-18T17:30:00Z",
        string status = "Planned")
    {
        return $$"""
            {"voyageId":"20000000-0000-0000-0000-000000000001","voyageNumber":"{{voyageNumber}}","vesselId":"30000000-0000-0000-0000-000000000002","departurePortCode":"MEBAR","arrivalPortCode":"ITGOA","status":"{{status}}","plannedDeparture":"{{plannedDeparture}}","plannedArrival":"{{plannedArrival}}","actualDeparture":null,"actualArrival":null}
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
