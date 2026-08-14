using System.Net;
using System.Text;
using HarborOps.Web.Api.Voyages;

namespace HarborOps.Web.Tests.Api;

public sealed class VoyagesApiClientTests
{
    private const string PageJson = """
        {"items":[{"voyageId":"20000000-0000-0000-0000-000000000001","voyageNumber":"VOY-600",
        "vesselId":"30000000-0000-0000-0000-000000000002","departurePortCode":"MEBAR",
        "arrivalPortCode":"ITGOA","status":"Planned","plannedDeparture":"2026-09-15T08:00:00Z",
        "plannedArrival":"2026-09-18T17:30:00Z","actualDeparture":null,"actualArrival":null}],
        "pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    [Fact]
    public async Task GetVoyagesAsync_sends_window_status_and_paging_to_the_voyages_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PageJson);
        });

        var result = await CreateClient(httpClient).GetVoyagesAsync(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            null,
            "Planned",
            false,
            2,
            50,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/voyages", captured.RequestUri!.AbsolutePath);
        Assert.Contains("from=2026-09-01", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("to=2026-09-30", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("status=Planned", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=50", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVoyagesAsync_sends_an_empty_status_when_no_status_is_selected()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PageJson);
        });

        await CreateClient(httpClient).GetVoyagesAsync(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            null,
            null,
            false,
            1,
            25,
            CancellationToken.None);

        Assert.Contains("status=&", captured!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVoyagesAsync_sends_the_search_term_escaped()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PageJson);
        });

        await CreateClient(httpClient).GetVoyagesAsync(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            "VOY 600",
            null,
            false,
            1,
            25,
            CancellationToken.None);

        Assert.Contains("search=VOY%20600", captured!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "includeActive=true")]
    [InlineData(false, "includeActive=false")]
    public async Task GetVoyagesAsync_sends_whether_active_voyages_stay_on_the_board(
        bool includeActive,
        string expected)
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PageJson);
        });

        await CreateClient(httpClient).GetVoyagesAsync(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            null,
            null,
            includeActive,
            1,
            25,
            CancellationToken.None);

        Assert.Contains(expected, captured!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVoyagesAsync_reads_rows_with_null_actual_dates()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, PageJson));

        var result = await CreateClient(httpClient).GetVoyagesAsync(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            null,
            null,
            false,
            1,
            25,
            CancellationToken.None);

        var voyage = Assert.Single(result.Value!.Items);

        Assert.Equal("VOY-600", voyage.VoyageNumber);
        Assert.Equal("MEBAR", voyage.DeparturePortCode);
        Assert.Equal("ITGOA", voyage.ArrivalPortCode);
        Assert.Null(voyage.ActualDeparture);
        Assert.Null(voyage.ActualArrival);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task GetVoyagesAsync_reads_an_empty_page_without_failing()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.OK,
            """{"items":[],"pageNumber":1,"pageSize":25,"totalCount":0}"""));

        var result = await CreateClient(httpClient).GetVoyagesAsync(
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30),
            null,
            null,
            false,
            1,
            25,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task GetVoyagesAsync_surfaces_validation_errors_from_problem_details()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"to":["To date must not be earlier than the from date."]}}"""));

        var result = await CreateClient(httpClient).GetVoyagesAsync(
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 9, 1),
            null,
            null,
            false,
            1,
            25,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Equal("One or more validation errors occurred.", result.Failure.Title);
        Assert.Equal(
            "To date must not be earlier than the from date.",
            Assert.Single(result.Failure.Errors["to"]));
    }

    [Fact]
    public async Task GetVoyagesAsync_returns_a_retryable_failure_when_the_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await CreateClient(httpClient).GetVoyagesAsync(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            null,
            null,
            false,
            1,
            25,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
        Assert.Empty(result.Failure.Errors);
    }

    [Fact]
    public async Task GetVoyagesAsync_can_be_called_again_after_a_failure()
    {
        var attempts = 0;
        using var httpClient = CreateHttpClient((_, _) =>
        {
            attempts++;
            return attempts == 1
                ? throw new HttpRequestException("Connection failed.")
                : Json(HttpStatusCode.OK, PageJson);
        });
        var client = CreateClient(httpClient);

        var firstResult = await client.GetVoyagesAsync(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, null, false, 1, 25, CancellationToken.None);
        var retryResult = await client.GetVoyagesAsync(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, null, false, 1, 25, CancellationToken.None);

        Assert.False(firstResult.IsSuccess);
        Assert.True(retryResult.IsSuccess);
        Assert.Equal(2, attempts);
        Assert.Single(retryResult.Value!.Items);
    }

    [Fact]
    public async Task GetVoyagesAsync_keeps_the_page_retryable_when_the_error_body_is_not_a_problem_document()
    {
        using var httpClient = CreateHttpClient((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("<html>gateway</html>", Encoding.UTF8, "text/html"),
            }));

        var result = await CreateClient(httpClient).GetVoyagesAsync(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            null,
            null,
            false,
            1,
            25,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(502, result.Failure!.StatusCode);
        Assert.Equal("Unable to load voyages", result.Failure.Title);
    }

    [Fact]
    public async Task ScheduleAsync_posts_the_voyage_to_the_collection_endpoint()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, _) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync();

            return await Json(HttpStatusCode.Created, """{"id":"70000000-0000-0000-0000-000000000004"}""");
        });

        var result = await CreateClient(httpClient).ScheduleAsync(
            new ScheduleVoyageWebRequest(
                "VOY-700",
                new Guid("40000000-0000-0000-0000-000000000009"),
                "MEBAR",
                "ITGOA",
                new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/voyages", captured.RequestUri!.AbsolutePath);
        Assert.Contains("2026-09-01T06:00:00Z", body, StringComparison.Ordinal);
        Assert.Equal(new Guid("70000000-0000-0000-0000-000000000004"), result.Value!.Id);
    }

    [Fact]
    public async Task RecordDepartureAsync_posts_to_the_departure_endpoint_and_reads_the_new_status()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;

            return Json(
                HttpStatusCode.OK,
                """{"id":"20000000-0000-0000-0000-000000000001","actualDepartureUtc":"2026-09-01T06:42:00Z","status":"InProgress"}""");
        });
        var voyageId = new Guid("20000000-0000-0000-0000-000000000001");

        var result = await CreateClient(httpClient).RecordDepartureAsync(
            voyageId,
            new RecordDepartureWebRequest(new DateTime(2026, 9, 1, 6, 42, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"/api/voyages/{voyageId}/departure", captured!.RequestUri!.AbsolutePath);
        Assert.Equal("InProgress", result.Value!.Status);
    }

    [Fact]
    public async Task RecordArrivalAsync_posts_to_the_arrival_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;

            return Json(
                HttpStatusCode.OK,
                """{"id":"20000000-0000-0000-0000-000000000001","actualArrivalUtc":"2026-09-04T18:05:00Z","status":"Completed"}""");
        });
        var voyageId = new Guid("20000000-0000-0000-0000-000000000001");

        var result = await CreateClient(httpClient).RecordArrivalAsync(
            voyageId,
            new RecordArrivalWebRequest(new DateTime(2026, 9, 4, 18, 5, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"/api/voyages/{voyageId}/arrival", captured!.RequestUri!.AbsolutePath);
        Assert.Equal("Completed", result.Value!.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true, false)]
    [InlineData(HttpStatusCode.Conflict, false, true)]
    public async Task RecordDepartureAsync_reports_the_authoritative_failure(
        HttpStatusCode statusCode,
        bool expectedNotFound,
        bool expectedConflict)
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            statusCode,
            """{"title":"The voyage cannot depart from its current status.","detail":"Voyage state changed."}"""));

        var result = await CreateClient(httpClient).RecordDepartureAsync(
            Guid.NewGuid(),
            new RecordDepartureWebRequest(new DateTime(2026, 9, 1, 6, 42, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedNotFound, result.Failure!.IsNotFound);
        Assert.Equal(expectedConflict, result.Failure.IsConflict);
        Assert.Equal("The voyage cannot depart from its current status.", result.Failure.Title);
    }

    private static VoyagesApiClient CreateClient(HttpClient httpClient) => new(httpClient);

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory)) { BaseAddress = new Uri("https://api.harborops.test/") };

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
