using System.Net;
using System.Text;
using HarborOps.Web.Api.Cargo;

namespace HarborOps.Web.Tests.Api;

public sealed class CargoBookingsApiClientTests
{
    private const string PageJson = """
        {"items":[{"cargoBookingId":"20000000-0000-0000-0000-000000000001","bookingNumber":"BK-2026-0143","voyageId":"30000000-0000-0000-0000-000000000002","description":"Ammonium nitrate","cargoType":"Dangerous Goods","weightKg":24000.000,"packageCount":480,"isHazardous":true,"status":"InTransit","createdUtc":"2026-08-11T09:42:00Z"}],"pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    private const string DetailsJson = """
        {"cargoBookingId":"20000000-0000-0000-0000-000000000001","bookingNumber":"BK-2026-0143","voyageId":"30000000-0000-0000-0000-000000000002","description":"Ammonium nitrate","cargoType":"Dangerous Goods","weightKg":24000.000,"packageCount":480,"isHazardous":true,"status":"Planned","nextStatus":"Loaded","createdUtc":"2026-08-11T09:42:00Z","safetyInfo":{"cargoSafetyInfoId":"40000000-0000-0000-0000-000000000003","imdgClass":"1.4S","unNumber":"UN1234","storageConditions":"Keep below 25 C","handlingNotes":null},"trackingEvents":[{"cargoTrackingEventId":"50000000-0000-0000-0000-000000000004","status":"Planned","occurredUtc":"2026-08-12T06:00:00Z","location":"Yard A","responsibleUserId":"60000000-0000-0000-0000-000000000005"}]}
        """;

    private static readonly Guid BookingId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static readonly Guid VoyageId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private static readonly DateTime OccurredUtc = new(2026, 8, 12, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SearchAsync_reads_every_booking_field()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, PageJson));

        var result = await CreateClient(httpClient).SearchAsync(Filter(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var row = Assert.Single(result.Value!.Items);

        Assert.Equal("BK-2026-0143", row.BookingNumber);
        Assert.Equal(Guid.Parse("30000000-0000-0000-0000-000000000002"), row.VoyageId);
        Assert.Equal("Ammonium nitrate", row.Description);
        Assert.Equal("Dangerous Goods", row.CargoType);
        Assert.Equal(24000.000m, row.WeightKg);
        Assert.Equal(480, row.PackageCount);
        Assert.True(row.IsHazardous);
        Assert.Equal("InTransit", row.Status);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_sends_only_the_filters_that_are_set()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PageJson);
        });

        await CreateClient(httpClient).SearchAsync(Filter(), CancellationToken.None);

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/cargo/bookings", captured.RequestUri!.AbsolutePath);
        Assert.Equal("?pageNumber=1&pageSize=25", captured.RequestUri.Query);
    }

    [Fact]
    public async Task SearchAsync_sends_search_status_and_hazardous_filters()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, PageJson);
        });

        var filter = new CargoBookingFilter(" ammonium nitrate ", "InTransit", true, 2, 50);

        await CreateClient(httpClient).SearchAsync(filter, CancellationToken.None);

        var query = captured!.RequestUri!.Query;

        Assert.Contains("search=ammonium%20nitrate", query, StringComparison.Ordinal);
        Assert.Contains("status=InTransit", query, StringComparison.Ordinal);
        Assert.Contains("isHazardous=true", query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", query, StringComparison.Ordinal);
        Assert.Contains("pageSize=50", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_requests_the_selected_page()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[],"pageNumber":3,"pageSize":25,"totalCount":60}""");
        });

        var result = await CreateClient(httpClient).SearchAsync(
            Filter() with { PageNumber = 3 },
            CancellationToken.None);

        Assert.Contains("pageNumber=3", captured!.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Equal(3, result.Value!.PageNumber);
        Assert.Equal(60, result.Value.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_returns_an_empty_page_without_data()
    {
        using var httpClient = CreateHttpClient(
            (_, _) => Json(HttpStatusCode.OK, """{"items":[],"pageNumber":1,"pageSize":25,"totalCount":0}"""));

        var result = await CreateClient(httpClient).SearchAsync(Filter(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_surfaces_validation_problem_details()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"status":["Status is not a known cargo status."]}}"""));

        var result = await CreateClient(httpClient).SearchAsync(
            Filter() with { Status = "Departed" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.False(result.Failure.IsForbidden);
        Assert.Equal(
            "Status is not a known cargo status.",
            Assert.Single(result.Failure.Errors["status"]));
    }

    [Fact]
    public async Task SearchAsync_reports_forbidden_without_the_cargo_read_permission()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, "{}"));

        var result = await CreateClient(httpClient).SearchAsync(Filter(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsForbidden);
        Assert.False(result.Failure.IsValidation);
    }

    [Fact]
    public async Task SearchAsync_returns_a_retryable_failure_when_the_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await CreateClient(httpClient).SearchAsync(Filter(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task SearchAsync_recovers_when_a_failure_carries_no_problem_details()
    {
        using var httpClient = CreateHttpClient((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("<html>gateway</html>", Encoding.UTF8, "text/html"),
            }));

        var result = await CreateClient(httpClient).SearchAsync(Filter(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(502, result.Failure!.StatusCode);
        Assert.Equal("Unable to load cargo bookings", result.Failure.Title);
    }

    [Fact]
    public async Task SearchAsync_succeeds_again_after_a_failed_attempt()
    {
        var attempt = 0;
        using var httpClient = CreateHttpClient((_, _) =>
        {
            attempt++;

            return attempt == 1
                ? throw new HttpRequestException("Connection failed.")
                : Json(HttpStatusCode.OK, PageJson);
        });

        var client = CreateClient(httpClient);
        var firstAttempt = await client.SearchAsync(Filter(), CancellationToken.None);
        var retry = await client.SearchAsync(Filter(), CancellationToken.None);

        Assert.False(firstAttempt.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.Single(retry.Value!.Items);
    }

    [Fact]
    public async Task GetDetailsAsync_reads_the_booking_safety_and_tracking_events()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, DetailsJson);
        });

        var result = await CreateClient(httpClient).GetDetailsAsync(BookingId, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal($"/api/cargo/bookings/{BookingId}", captured.RequestUri!.AbsolutePath);
        Assert.True(result.IsSuccess);

        var details = result.Value!;

        Assert.Equal("BK-2026-0143", details.BookingNumber);
        Assert.Equal("Planned", details.Status);
        Assert.Equal("Loaded", details.NextStatus);
        Assert.NotNull(details.SafetyInfo);
        Assert.Equal("1.4S", details.SafetyInfo.ImdgClass);
        Assert.Equal("UN1234", details.SafetyInfo.UnNumber);
        Assert.Null(details.SafetyInfo.HandlingNotes);

        var trackingEvent = Assert.Single(details.TrackingEvents);

        Assert.Equal("Planned", trackingEvent.Status);
        Assert.Equal("Yard A", trackingEvent.Location);
    }

    [Fact]
    public async Task GetDetailsAsync_reads_a_booking_without_safety_or_tracking_records()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.OK,
            """{"cargoBookingId":"20000000-0000-0000-0000-000000000001","bookingNumber":"BK-2026-0144","voyageId":"30000000-0000-0000-0000-000000000002","description":"Steel coils","cargoType":"General Cargo","weightKg":100.000,"packageCount":2,"isHazardous":false,"status":"Completed","nextStatus":null,"createdUtc":"2026-08-11T09:42:00Z","safetyInfo":null,"trackingEvents":[]}"""));

        var result = await CreateClient(httpClient).GetDetailsAsync(BookingId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.SafetyInfo);
        Assert.Null(result.Value.NextStatus);
        Assert.Empty(result.Value.TrackingEvents);
    }

    [Fact]
    public async Task GetDetailsAsync_reports_a_missing_booking()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.NotFound,
            """{"title":"Cargo booking not found.","status":404}"""));

        var result = await CreateClient(httpClient).GetDetailsAsync(BookingId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
        Assert.Equal("Cargo booking not found.", result.Failure.Title);
    }

    [Fact]
    public async Task RegisterAsync_posts_the_booking_and_reads_the_identifier()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.Created, $$"""{"id":"{{BookingId}}"}""");
        });

        var result = await CreateClient(httpClient).RegisterAsync(
            new RegisterCargoBookingWebRequest(
                "BK-2026-0145",
                VoyageId,
                "Steel coils",
                "General Cargo",
                5000.000m,
                10,
                false),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/cargo/bookings", captured.RequestUri!.AbsolutePath);
        Assert.True(result.IsSuccess);
        Assert.Equal(BookingId, result.Value!.Id);
    }

    [Fact]
    public async Task RegisterAsync_reports_a_duplicate_booking_number()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"A cargo booking with this booking number already exists.","status":409,"errors":{"bookingNumber":["Already used."]}}"""));

        var result = await CreateClient(httpClient).RegisterAsync(
            new RegisterCargoBookingWebRequest(
                "BK-2026-0145",
                VoyageId,
                "Steel coils",
                "General Cargo",
                5000.000m,
                10,
                false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Already used.", Assert.Single(result.Failure.Errors["bookingNumber"]));
    }

    [Fact]
    public async Task DeclareSafetyInformationAsync_posts_to_the_booking_safety_route()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.Created, $$"""{"id":"{{BookingId}}"}""");
        });

        var result = await CreateClient(httpClient).DeclareSafetyInformationAsync(
            BookingId,
            new DeclareCargoSafetyInfoWebRequest("1.4S", "UN1234", "Keep below 25 C", null),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(
            $"/api/cargo/bookings/{BookingId}/safety-information",
            captured.RequestUri!.AbsolutePath);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeclareSafetyInformationAsync_reports_a_non_hazardous_booking()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Cargo booking is not hazardous.","status":409}"""));

        var result = await CreateClient(httpClient).DeclareSafetyInformationAsync(
            BookingId,
            new DeclareCargoSafetyInfoWebRequest("1.4S", "UN1234", "Keep below 25 C", null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Cargo booking is not hazardous.", result.Failure.Title);
    }

    [Fact]
    public async Task RecordTrackingEventAsync_posts_to_the_booking_tracking_route()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.Created, $$"""{"id":"{{BookingId}}","status":"Loaded"}""");
        });

        var result = await CreateClient(httpClient).RecordTrackingEventAsync(
            BookingId,
            new RecordCargoTrackingEventWebRequest("Loaded", OccurredUtc, "Berth 12"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(
            $"/api/cargo/bookings/{BookingId}/tracking-events",
            captured.RequestUri!.AbsolutePath);
        Assert.True(result.IsSuccess);
        Assert.Equal("Loaded", result.Value!.Status);
    }

    [Fact]
    public async Task RecordTrackingEventAsync_reports_a_rejected_transition()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Cargo status transition is not allowed.","status":409}"""));

        var result = await CreateClient(httpClient).RecordTrackingEventAsync(
            BookingId,
            new RecordCargoTrackingEventWebRequest("Completed", OccurredUtc, "Berth 12"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Cargo status transition is not allowed.", result.Failure.Title);
    }

    [Fact]
    public async Task RecordTrackingEventAsync_returns_a_retryable_failure_when_the_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await CreateClient(httpClient).RecordTrackingEventAsync(
            BookingId,
            new RecordCargoTrackingEventWebRequest("Loaded", OccurredUtc, "Berth 12"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    private static CargoBookingFilter Filter()
    {
        return new CargoBookingFilter(null, null, null, 1, 25);
    }

    private static CargoBookingsApiClient CreateClient(HttpClient httpClient)
    {
        return new CargoBookingsApiClient(httpClient);
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        return new HttpClient(new StubHttpMessageHandler(responseFactory))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });
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
