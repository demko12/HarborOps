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
            "Planned",
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
            1,
            25,
            CancellationToken.None);

        Assert.Contains("status=&", captured!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVoyagesAsync_reads_rows_with_null_actual_dates()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, PageJson));

        var result = await CreateClient(httpClient).GetVoyagesAsync(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            null,
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
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, 1, 25, CancellationToken.None);
        var retryResult = await client.GetVoyagesAsync(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, 1, 25, CancellationToken.None);

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
            1,
            25,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(502, result.Failure!.StatusCode);
        Assert.Equal("Unable to load voyages", result.Failure.Title);
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
