using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api.Cargo;
using HarborOps.Web.Components.Pages.Cargo;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class CargoPageTests : BunitContext
{
    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, Page());

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

    private IRenderedComponent<CargoBookings> RenderCargoPage()
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

        var page = Render<CargoBookings>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
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
