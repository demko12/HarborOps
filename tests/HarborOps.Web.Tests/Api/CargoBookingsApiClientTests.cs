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
        {"cargoBookingId":"20000000-0000-0000-0000-000000000001","bookingNumber":"BK-2026-0143","voyageId":"30000000-0000-0000-0000-000000000002","description":"Ammonium nitrate","cargoType":"Dangerous Goods","weightKg":24000.000,"packageCount":480,"isHazardous":true,"status":"Planned","nextStatus":"Loaded","createdUtc":"2026-08-11T09:42:00Z","safetyInfo":{"cargoSafetyInfoId":"40000000-0000-0000-0000-000000000003","imdgClass":"1.4S","unNumber":"UN1234","storageConditions":"Keep below 25 C","handlingNotes":null,"reviewStatus":"Approved","submittedByUserId":"70000000-0000-0000-0000-000000000006","submittedAtUtc":"2026-09-01T10:00:00Z","reviewedByUserId":"70000000-0000-0000-0000-000000000006","reviewedAtUtc":"2026-09-01T14:30:00Z","reviewNotes":null},"trackingEvents":[{"cargoTrackingEventId":"50000000-0000-0000-0000-000000000004","status":"Planned","occurredUtc":"2026-08-12T06:00:00Z","location":"Yard A","responsibleUserId":"60000000-0000-0000-0000-000000000005"}]}
        """;

    private const string DeclarationJson = """
        {"cargoSafetyInfoId":"40000000-0000-0000-0000-000000000003","imdgClass":"1.4S","unNumber":"UN1234","storageConditions":"Keep below 25 C","handlingNotes":null,"reviewStatus":"Submitted","submittedByUserId":"70000000-0000-0000-0000-000000000006","submittedAtUtc":"2026-09-01T10:00:00Z","reviewedByUserId":null,"reviewedAtUtc":null,"reviewNotes":null}
        """;

    private const string SafetyInformationJson = """
        {"declaration":{"cargoSafetyInfoId":"40000000-0000-0000-0000-000000000003","imdgClass":"1.4S","unNumber":"UN1234","storageConditions":"Keep below 25 C","handlingNotes":null,"reviewStatus":"Submitted","submittedByUserId":"70000000-0000-0000-0000-000000000006","submittedAtUtc":"2026-09-01T10:00:00Z","reviewedByUserId":null,"reviewedAtUtc":null,"reviewNotes":null},"timeline":[{"cargoSafetyReviewId":"90000000-0000-0000-0000-000000000008","fromStatus":"Draft","toStatus":"Submitted","actorUserId":"70000000-0000-0000-0000-000000000006","occurredUtc":"2026-09-01T10:00:00Z","notes":null}]}
        """;

    private const string ContainerControlJson = """
        {"containerId":"a0000000-0000-0000-0000-000000000009","containerNumber":"MSKU1234567","containerType":"40RH","isReefer":true,"sealNumber":"ML458821","sealStatus":"Intact","state":"Released","latestInspectionResult":"Passed","latestInspectionNotes":"Cooling steady","latestInspectionTemperatureCelsius":-18.5,"latestInspectedAtUtc":"2026-09-07T09:15:00Z","latestInspectedByUserId":"70000000-0000-0000-0000-000000000006","stateReason":"Cleared for loading","stateChangedByUserId":"70000000-0000-0000-0000-000000000006","stateChangedAtUtc":"2026-09-07T10:30:00Z","canRelease":false,"releaseBlockedReason":"the container is already released"}
        """;

    private const string ContainerControlViewJson = """
        {"control":{"containerId":"a0000000-0000-0000-0000-000000000009","containerNumber":"MSKU1234567","containerType":"40RH","isReefer":true,"sealNumber":"ML458821","sealStatus":"Intact","state":"Released","latestInspectionResult":"Passed","latestInspectionNotes":"Cooling steady","latestInspectionTemperatureCelsius":-18.5,"latestInspectedAtUtc":"2026-09-07T09:15:00Z","latestInspectedByUserId":"70000000-0000-0000-0000-000000000006","stateReason":"Cleared for loading","stateChangedByUserId":"70000000-0000-0000-0000-000000000006","stateChangedAtUtc":"2026-09-07T10:30:00Z","canRelease":false,"releaseBlockedReason":"the container is already released"},"history":[{"containerControlEventId":"b0000000-0000-0000-0000-000000000010","action":"SealAssigned","fromState":"Pending","toState":"Pending","sealNumber":"ML458821","inspectionResult":null,"temperatureCelsius":null,"notes":null,"actorUserId":"70000000-0000-0000-0000-000000000006","occurredUtc":"2026-09-07T08:00:00Z"}]}
        """;

    private const string ReeferProfileViewJson = """
        {"containerId":"a0000000-0000-0000-0000-000000000009","containerNumber":"MSKU1234567","profile":{"containerId":"a0000000-0000-0000-0000-000000000009","minimumCelsius":-2,"maximumCelsius":4,"targetCelsius":1,"version":2,"createdByUserId":"70000000-0000-0000-0000-000000000006","createdAtUtc":"2026-09-18T06:00:00Z","updatedByUserId":"70000000-0000-0000-0000-000000000006","updatedAtUtc":"2026-09-19T16:00:00Z"},"changes":[{"id":"c0000000-0000-0000-0000-000000000011","version":2,"fromMinimumCelsius":-5,"fromMaximumCelsius":2,"fromTargetCelsius":-1,"toMinimumCelsius":-2,"toMaximumCelsius":4,"toTargetCelsius":1,"reason":"Shipper tightened the cold chain","actorUserId":"70000000-0000-0000-0000-000000000006","occurredUtc":"2026-09-19T16:00:00Z"}],"openExcursionCount":1}
        """;

    private const string ReeferReadingPageJson = """
        {"items":[{"id":"d0000000-0000-0000-0000-000000000012","containerId":"a0000000-0000-0000-0000-000000000009","temperatureCelsius":1,"measuredAtUtc":"2026-09-19T07:45:00Z","recordedAtUtc":"2026-09-19T08:00:00Z","notes":null,"recordedByUserId":"70000000-0000-0000-0000-000000000006","thresholdMinimumCelsius":-2,"thresholdMaximumCelsius":4,"thresholdTargetCelsius":1,"thresholdProfileVersion":1,"status":"Effective","isWithinThresholds":true,"correctsReadingId":null,"supersededByReadingId":null,"correctionReason":null,"correctedByUserId":null,"correctedAtUtc":null,"voidReason":null,"voidedByUserId":null,"voidedAtUtc":null}],"pageNumber":2,"pageSize":10,"totalCount":24}
        """;

    private const string RecordedReeferReadingJson = """
        {"reading":{"id":"d0000000-0000-0000-0000-000000000012","containerId":"a0000000-0000-0000-0000-000000000009","temperatureCelsius":6.5,"measuredAtUtc":"2026-09-19T07:45:00Z","recordedAtUtc":"2026-09-19T08:00:00Z","notes":null,"recordedByUserId":"70000000-0000-0000-0000-000000000006","thresholdMinimumCelsius":-2,"thresholdMaximumCelsius":4,"thresholdTargetCelsius":1,"thresholdProfileVersion":1,"status":"Effective","isWithinThresholds":false,"correctsReadingId":null,"supersededByReadingId":null,"correctionReason":null,"correctedByUserId":null,"correctedAtUtc":null,"voidReason":null,"voidedByUserId":null,"voidedAtUtc":null},"excursion":{"id":"e0000000-0000-0000-0000-000000000013","containerId":"a0000000-0000-0000-0000-000000000009","readingId":"d0000000-0000-0000-0000-000000000012","direction":"Above","temperatureCelsius":6.5,"thresholdMinimumCelsius":-2,"thresholdMaximumCelsius":4,"measuredAtUtc":"2026-09-19T07:45:00Z","detectedAtUtc":"2026-09-19T08:00:00Z","status":"Open","disposition":null,"explanation":null,"resolvedByUserId":null,"resolvedAtUtc":null,"supersededByExcursionId":null,"supersededAtUtc":null}}
        """;

    private static readonly Guid BookingId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static readonly Guid ReeferExcursionId =
        Guid.Parse("e0000000-0000-0000-0000-000000000013");

    private static readonly Guid ControlContainerId =
        Guid.Parse("a0000000-0000-0000-0000-000000000009");

    private static readonly Guid SubmittedByUserId =
        Guid.Parse("70000000-0000-0000-0000-000000000006");

    private static readonly Guid VoyageId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private static readonly Guid OperationId = Guid.Parse("80000000-0000-0000-0000-000000000007");

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
    public async Task GetSafetyInformationAsync_reads_the_declaration_and_the_timeline()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, SafetyInformationJson);
        });

        var result = await CreateClient(httpClient).GetSafetyInformationAsync(
            BookingId,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal(
            $"/api/cargo/bookings/{BookingId}/safety-information",
            captured.RequestUri!.AbsolutePath);
        Assert.True(result.IsSuccess);
        Assert.Equal("Submitted", result.Value!.Declaration.ReviewStatus);
        Assert.Equal(SubmittedByUserId, result.Value.Declaration.SubmittedByUserId);
        Assert.Null(result.Value.Declaration.ReviewedAtUtc);

        var entry = Assert.Single(result.Value.Timeline);

        Assert.Equal("Draft", entry.FromStatus);
        Assert.Equal("Submitted", entry.ToStatus);
        Assert.Equal(SubmittedByUserId, entry.ActorUserId);
    }

    [Fact]
    public async Task GetSafetyInformationAsync_reports_a_missing_declaration()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.NotFound,
            """{"title":"Cargo safety information not found.","status":404}"""));

        var result = await CreateClient(httpClient).GetSafetyInformationAsync(
            BookingId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
        Assert.Equal("Cargo safety information not found.", result.Failure.Title);
    }

    [Fact]
    public async Task SaveSafetyInformationAsync_puts_the_declaration_to_the_safety_route()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);

            return await Json(HttpStatusCode.OK, DeclarationJson);
        });

        var result = await CreateClient(httpClient).SaveSafetyInformationAsync(
            BookingId,
            new UpdateCargoSafetyInfoWebRequest("1.4S", "UN1234", "Keep below 25 C", null),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.Equal(
            $"/api/cargo/bookings/{BookingId}/safety-information",
            captured.RequestUri!.AbsolutePath);
        Assert.Contains("\"imdgClass\":\"1.4S\"", body, StringComparison.Ordinal);
        Assert.True(result.IsSuccess);
        Assert.Equal("Submitted", result.Value!.ReviewStatus);
    }

    [Fact]
    public async Task SaveSafetyInformationAsync_reports_the_invalid_fields()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"unNumber":["UN number must be 4 digits, optionally prefixed with 'UN'."]}}"""));

        var result = await CreateClient(httpClient).SaveSafetyInformationAsync(
            BookingId,
            new UpdateCargoSafetyInfoWebRequest("1.4S", "12A4", "Keep below 25 C", null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Single(result.Failure.Errors["unNumber"]);
    }

    [Fact]
    public async Task SaveSafetyInformationAsync_reports_a_non_hazardous_booking()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Cargo booking is not hazardous.","status":409}"""));

        var result = await CreateClient(httpClient).SaveSafetyInformationAsync(
            BookingId,
            new UpdateCargoSafetyInfoWebRequest("1.4S", "UN1234", "Keep below 25 C", null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Cargo booking is not hazardous.", result.Failure.Title);
    }

    [Fact]
    public async Task SubmitSafetyInformationAsync_posts_to_the_submit_route()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, DeclarationJson);
        });

        var result = await CreateClient(httpClient).SubmitSafetyInformationAsync(
            BookingId,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(
            $"/api/cargo/bookings/{BookingId}/safety-information/submit",
            captured.RequestUri!.AbsolutePath);
        Assert.True(result.IsSuccess);
        Assert.Equal("Submitted", result.Value!.ReviewStatus);
    }

    [Fact]
    public async Task SubmitSafetyInformationAsync_reports_a_transition_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Cargo safety information transition is not allowed.","status":409}"""));

        var result = await CreateClient(httpClient).SubmitSafetyInformationAsync(
            BookingId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Cargo safety information transition is not allowed.", result.Failure.Title);
    }

    [Fact]
    public async Task ReviewSafetyInformationAsync_posts_the_decision_to_the_review_route()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);

            return await Json(HttpStatusCode.OK, DeclarationJson);
        });

        var result = await CreateClient(httpClient).ReviewSafetyInformationAsync(
            BookingId,
            new ReviewCargoSafetyInfoWebRequest("Rejected", "Storage conditions are unclear."),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(
            $"/api/cargo/bookings/{BookingId}/safety-information/review",
            captured.RequestUri!.AbsolutePath);
        Assert.Contains("\"status\":\"Rejected\"", body, StringComparison.Ordinal);
        Assert.Contains("Storage conditions are unclear.", body, StringComparison.Ordinal);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ReviewSafetyInformationAsync_reports_missing_review_notes()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"notes":["Review notes are required when a declaration is rejected."]}}"""));

        var result = await CreateClient(httpClient).ReviewSafetyInformationAsync(
            BookingId,
            new ReviewCargoSafetyInfoWebRequest("Rejected", null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Single(result.Failure.Errors["notes"]);
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

    [Fact]
    public async Task GetContainersAsync_reads_every_container_field()
    {
        const string json = """
            {"items":[{"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","containerType":"40HC","status":"Loaded","cargoBookingId":"20000000-0000-0000-0000-000000000001","vesselId":null}]}
            """;
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, json));

        var result = await CreateClient(httpClient).GetContainersAsync(
            BookingId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var row = Assert.Single(result.Value!.Items);

        Assert.Equal(Guid.Parse("70000000-0000-0000-0000-000000000006"), row.ContainerId);
        Assert.Equal("MSKU1234567", row.ContainerNumber);
        Assert.Equal("40HC", row.ContainerType);
        Assert.Equal("Loaded", row.Status);
        Assert.Equal(BookingId, row.CargoBookingId);
        Assert.Null(row.VesselId);
    }

    [Fact]
    public async Task GetContainersAsync_requests_the_manifest_of_the_booking()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[]}""");
        });

        await CreateClient(httpClient).GetContainersAsync(BookingId, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal($"/api/cargo/bookings/{BookingId}/containers", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetContainersAsync_sends_every_applied_filter_with_the_page()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[]}""");
        });

        await CreateClient(httpClient).GetContainersAsync(
            BookingId,
            CargoContainerFilter.Create(" msku 12&3 ", "Loaded", "42r1", "nlrtm", " usnyc"),
            3,
            CancellationToken.None);

        Assert.Equal(
            "?search=MSKU%2012%263&status=Loaded&containerType=42R1&portOfLoadingCode=NLRTM&portOfDischargeCode=USNYC&pageNumber=3&pageSize=15",
            captured!.RequestUri!.Query);
    }

    [Fact]
    public async Task GetContainersAsync_leaves_out_blank_filters()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[]}""");
        });

        await CreateClient(httpClient).GetContainersAsync(
            BookingId,
            CargoContainerFilter.Create("  ", "", null, " ", null),
            1,
            CancellationToken.None);

        Assert.Equal("?pageNumber=1&pageSize=15", captured!.RequestUri!.Query);
    }

    [Fact]
    public async Task GetContainersAsync_reads_the_matching_and_whole_booking_counts()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.OK,
            """{"items":[],"totalCount":40,"pageNumber":2,"pageSize":15,"loadedCount":12,"matchingCount":17}"""));

        var result = await CreateClient(httpClient).GetContainersAsync(
            BookingId,
            CargoContainerFilter.Create("MSKU", null, null, null, null),
            2,
            CancellationToken.None);

        Assert.Equal(40, result.Value!.TotalCount);
        Assert.Equal(12, result.Value.LoadedCount);
        Assert.Equal(17, result.Value.MatchingCount);
        Assert.Equal(2, result.Value.PageNumber);
    }

    [Fact]
    public async Task GetContainersAsync_reports_the_rejected_filter_fields()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"portOfLoadingCode":["POL must be a five-character UN/LOCODE."]}}"""));

        var result = await CreateClient(httpClient).GetContainersAsync(
            BookingId,
            CargoContainerFilter.Create(null, null, null, "NL", null),
            1,
            CancellationToken.None);

        Assert.True(result.Failure!.IsValidation);
        Assert.Equal(["POL must be a five-character UN/LOCODE."], result.Failure.Errors["portOfLoadingCode"]);
    }

    [Fact]
    public async Task GetManifestPrintAsync_requests_the_print_model_with_the_filters_and_no_page()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(
                HttpStatusCode.OK,
                """{"cargoBookingId":"20000000-0000-0000-0000-000000000001","bookingNumber":"BK-1","generatedAtUtc":"2026-09-28T14:32:05Z","filters":{"search":"MSKU","status":null,"containerType":null,"portOfLoadingCode":null,"portOfDischargeCode":null},"isFiltered":true,"matchingCount":1,"bookingContainerCount":3,"knownWeightKg":12500.5,"knownWeightCount":1,"unknownWeightCount":0,"rowLimit":10000,"rows":[{"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","containerType":"42R1","status":"Loaded","portOfLoadingCode":null,"portOfDischargeCode":null,"weightKg":12500.5,"vesselId":null}]}""");
        });

        var result = await CreateClient(httpClient).GetManifestPrintAsync(
            BookingId,
            CargoContainerFilter.Create(" msku ", null, null, null, null),
            CancellationToken.None);

        Assert.Equal($"/api/cargo/bookings/{BookingId}/containers/print?search=MSKU", captured!.RequestUri!.PathAndQuery);
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.BookingContainerCount);
        Assert.Equal(12500.5m, result.Value.KnownWeightKg);
        Assert.Equal(DateTimeKind.Utc, result.Value.GeneratedAtUtc.Kind);
        Assert.Null(Assert.Single(result.Value.Rows).PortOfLoadingCode);
    }

    [Fact]
    public async Task GetManifestPrintAsync_reports_the_row_limit()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"limit":["More than 10,000 containers match. Narrow the filters to export or print at most 10,000 containers."]}}"""));

        var result = await CreateClient(httpClient).GetManifestPrintAsync(BookingId, CargoContainerFilter.None, CancellationToken.None);

        Assert.True(result.Failure!.IsValidation);
        Assert.Single(result.Failure.Errors["limit"]);
    }

    [Fact]
    public async Task DownloadManifestCsvAsync_requests_the_export_without_a_query_when_unfiltered()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, string.Empty);
        });

        using var response = await CreateClient(httpClient).DownloadManifestCsvAsync(BookingId, CargoContainerFilter.None, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal($"/api/cargo/bookings/{BookingId}/containers/export", captured.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task GetContainersAsync_reports_a_forbidden_failure()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, "{}"));

        var result = await CreateClient(httpClient).GetContainersAsync(
            BookingId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsForbidden);
        Assert.Equal("Unable to load the container manifest", result.Failure.Title);
    }

    [Fact]
    public async Task AddContainerAsync_posts_the_container_to_the_booking()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);

            return await Json(
                HttpStatusCode.Created,
                """{"id":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","status":"Registered"}""");
        });

        var result = await CreateClient(httpClient).AddContainerAsync(
            BookingId,
            new AddCargoContainerWebRequest("MSKU1234567", "40HC"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("MSKU1234567", result.Value!.ContainerNumber);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/cargo/bookings/{BookingId}/containers", captured.RequestUri!.AbsolutePath);
        Assert.Contains("\"containerNumber\":\"MSKU1234567\"", body, StringComparison.Ordinal);
        Assert.Contains("\"containerType\":\"40HC\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddContainerAsync_reports_a_duplicate_number_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """
            {"title":"A container with this number already exists.","status":409,"errors":{"containerNumber":["Already registered."]}}
            """));

        var result = await CreateClient(httpClient).AddContainerAsync(
            BookingId,
            new AddCargoContainerWebRequest("MSKU1234567", "40HC"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Contains("containerNumber", result.Failure.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetOperationsAsync_reads_every_operation_field()
    {
        const string json = """
            {"items":[{"operationId":"80000000-0000-0000-0000-000000000007","operationType":"Load","operationState":"InProgress","plannedAtUtc":"2026-08-12T06:00:00Z","startedAtUtc":"2026-08-12T07:30:00Z","finishedAtUtc":null,"responsibleUserId":"60000000-0000-0000-0000-000000000005"}]}
            """;
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, json));

        var result = await CreateClient(httpClient).GetOperationsAsync(
            BookingId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var row = Assert.Single(result.Value!.Items);

        Assert.Equal(Guid.Parse("80000000-0000-0000-0000-000000000007"), row.OperationId);
        Assert.Equal("Load", row.OperationType);
        Assert.Equal("InProgress", row.OperationState);
        Assert.Equal(OccurredUtc, row.PlannedAtUtc);
        Assert.NotNull(row.StartedAtUtc);
        Assert.Null(row.FinishedAtUtc);
        Assert.Equal(Guid.Parse("60000000-0000-0000-0000-000000000005"), row.ResponsibleUserId);
    }

    [Fact]
    public async Task PlanOperationAsync_posts_the_operation_in_utc()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);

            return await Json(
                HttpStatusCode.Created,
                """{"id":"80000000-0000-0000-0000-000000000007","operationType":"Load","operationState":"Planned"}""");
        });

        var result = await CreateClient(httpClient).PlanOperationAsync(
            BookingId,
            new PlanCargoOperationWebRequest("Load", OccurredUtc),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Planned", result.Value!.OperationState);
        Assert.Equal($"/api/cargo/bookings/{BookingId}/operations", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("\"operationType\":\"Load\"", body, StringComparison.Ordinal);
        Assert.Contains("2026-08-12T06:00:00Z", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartOperationAsync_posts_to_the_operation_without_a_body()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;

            return Json(
                HttpStatusCode.OK,
                """{"operationId":"80000000-0000-0000-0000-000000000007","operationState":"InProgress","startedAtUtc":"2026-08-12T07:30:00Z","finishedAtUtc":null}""");
        });

        var result = await CreateClient(httpClient).StartOperationAsync(
            OperationId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("InProgress", result.Value!.OperationState);
        Assert.Null(result.Value.FinishedAtUtc);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/cargo/operations/{OperationId}/start", captured.RequestUri!.AbsolutePath);
        Assert.Null(captured.Content);
    }

    [Fact]
    public async Task StartOperationAsync_reports_a_transition_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """
            {"title":"Cargo operation transition is not allowed.","status":409,"detail":"It is already in progress."}
            """));

        var result = await CreateClient(httpClient).StartOperationAsync(
            OperationId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Cargo operation transition is not allowed.", result.Failure.Title);
    }

    [Fact]
    public async Task CompleteOperationAsync_posts_to_the_operation_without_a_body()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;

            return Json(
                HttpStatusCode.OK,
                """{"operationId":"80000000-0000-0000-0000-000000000007","operationState":"Completed","startedAtUtc":"2026-08-12T07:30:00Z","finishedAtUtc":"2026-08-12T09:15:00Z"}""");
        });

        var result = await CreateClient(httpClient).CompleteOperationAsync(
            OperationId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.OperationState);
        Assert.NotNull(result.Value.FinishedAtUtc);
        Assert.Equal($"/api/cargo/operations/{OperationId}/complete", captured!.RequestUri!.AbsolutePath);
        Assert.Null(captured.Content);
    }

    [Fact]
    public async Task CompleteOperationAsync_returns_a_retryable_failure_when_the_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await CreateClient(httpClient).CompleteOperationAsync(
            OperationId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task GetContainerControlAsync_returns_the_control_and_its_history()
    {
        using var httpClient = CreateHttpClient((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                $"/api/cargo/containers/{ControlContainerId}/control",
                request.RequestUri!.AbsolutePath);

            return Json(HttpStatusCode.OK, ContainerControlViewJson);
        });

        var result = await CreateClient(httpClient).GetContainerControlAsync(
            ControlContainerId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ML458821", result.Value!.Control.SealNumber);
        Assert.Equal("Released", result.Value.Control.State);
        Assert.True(result.Value.Control.IsReefer);
        Assert.Equal(-18.5m, result.Value.Control.LatestInspectionTemperatureCelsius);
        Assert.Equal("SealAssigned", Assert.Single(result.Value.History).Action);
    }

    [Fact]
    public async Task AssignContainerSealAsync_posts_the_seal_number()
    {
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                $"/api/cargo/containers/{ControlContainerId}/seal",
                request.RequestUri!.AbsolutePath);
            Assert.Contains(
                "ML458821",
                await request.Content!.ReadAsStringAsync(token),
                StringComparison.Ordinal);

            return await Json(HttpStatusCode.OK, ContainerControlJson);
        });

        var result = await CreateClient(httpClient).AssignContainerSealAsync(
            ControlContainerId,
            new AssignContainerSealWebRequest("ML458821"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Intact", result.Value!.SealStatus);
    }

    [Fact]
    public async Task BreakContainerSealAsync_posts_to_the_seal_break_endpoint()
    {
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                $"/api/cargo/containers/{ControlContainerId}/seal/break",
                request.RequestUri!.AbsolutePath);
            Assert.Contains(
                "Opened for customs",
                await request.Content!.ReadAsStringAsync(token),
                StringComparison.Ordinal);

            return await Json(HttpStatusCode.OK, ContainerControlJson);
        });

        var result = await CreateClient(httpClient).BreakContainerSealAsync(
            ControlContainerId,
            new BreakContainerSealWebRequest("Opened for customs"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task BreakContainerSealAsync_reports_a_missing_seal_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Container control transition is not allowed.","status":409}"""));

        var result = await CreateClient(httpClient).BreakContainerSealAsync(
            ControlContainerId,
            new BreakContainerSealWebRequest(null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
    }

    [Fact]
    public async Task AssignContainerSealAsync_reports_a_duplicate_seal_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Seal number is already in use.","status":409,"detail":"Seal number 'ML458821' is already active on another container."}"""));

        var result = await CreateClient(httpClient).AssignContainerSealAsync(
            ControlContainerId,
            new AssignContainerSealWebRequest("ML458821"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Seal number is already in use.", result.Failure.Title);
    }

    [Fact]
    public async Task RecordContainerInspectionAsync_posts_the_result_and_the_temperature()
    {
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            Assert.Equal(
                $"/api/cargo/containers/{ControlContainerId}/inspections",
                request.RequestUri!.AbsolutePath);

            var body = await request.Content!.ReadAsStringAsync(token);

            Assert.Contains("Passed", body, StringComparison.Ordinal);
            Assert.Contains("-18.5", body, StringComparison.Ordinal);

            return await Json(HttpStatusCode.Created, ContainerControlJson);
        });

        var result = await CreateClient(httpClient).RecordContainerInspectionAsync(
            ControlContainerId,
            new RecordContainerInspectionWebRequest("Passed", "Cooling steady", -18.5m),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Passed", result.Value!.LatestInspectionResult);
    }

    [Fact]
    public async Task RecordContainerInspectionAsync_surfaces_field_validation_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"temperatureCelsius":["A temperature can only be recorded for a reefer container."]}}"""));

        var result = await CreateClient(httpClient).RecordContainerInspectionAsync(
            ControlContainerId,
            new RecordContainerInspectionWebRequest("Passed", null, -18.5m),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Contains("temperatureCelsius", result.Failure.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ReleaseContainerAsync_posts_the_notes()
    {
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            Assert.Equal(
                $"/api/cargo/containers/{ControlContainerId}/release",
                request.RequestUri!.AbsolutePath);
            Assert.Contains(
                "Cleared for loading",
                await request.Content!.ReadAsStringAsync(token),
                StringComparison.Ordinal);

            return await Json(HttpStatusCode.OK, ContainerControlJson);
        });

        var result = await CreateClient(httpClient).ReleaseContainerAsync(
            ControlContainerId,
            new ReleaseContainerWebRequest("Cleared for loading"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Released", result.Value!.State);
    }

    [Fact]
    public async Task ReleaseContainerAsync_reports_a_blocked_release_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Container control transition is not allowed.","status":409,"detail":"The container is held."}"""));

        var result = await CreateClient(httpClient).ReleaseContainerAsync(
            ControlContainerId,
            new ReleaseContainerWebRequest(null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
    }

    [Fact]
    public async Task HoldContainerAsync_posts_the_reason()
    {
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            Assert.Equal(
                $"/api/cargo/containers/{ControlContainerId}/hold",
                request.RequestUri!.AbsolutePath);
            Assert.Contains(
                "Damaged corner post",
                await request.Content!.ReadAsStringAsync(token),
                StringComparison.Ordinal);

            return await Json(HttpStatusCode.OK, ContainerControlJson);
        });

        var result = await CreateClient(httpClient).HoldContainerAsync(
            ControlContainerId,
            new HoldContainerWebRequest("Damaged corner post"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetContainerControlAsync_reports_an_unknown_container_as_not_found()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.NotFound,
            """{"title":"Container not found.","status":404}"""));

        var result = await CreateClient(httpClient).GetContainerControlAsync(
            ControlContainerId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
    }

    [Fact]
    public async Task GetReeferProfileAsync_reads_the_profile_and_change_log()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Json(HttpStatusCode.OK, ReeferProfileViewJson));

        var result = await CreateClient(httpClient).GetReeferProfileAsync(
            ControlContainerId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Profile);
        Assert.Equal(-2m, result.Value.Profile!.MinimumCelsius);
        Assert.Equal(2, result.Value.Profile.Version);
        Assert.Single(result.Value.Changes);
        Assert.Equal(1, result.Value.OpenExcursionCount);
    }

    [Fact]
    public async Task GetReeferReadingsAsync_requests_a_bounded_page()
    {
        Uri? requested = null;

        using var httpClient = CreateHttpClient((request, _) =>
        {
            requested = request.RequestUri;

            return Json(HttpStatusCode.OK, ReeferReadingPageJson);
        });

        var result = await CreateClient(httpClient).GetReeferReadingsAsync(
            ControlContainerId,
            2,
            10,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            $"/api/cargo/containers/{ControlContainerId}/reefer/readings",
            requested!.AbsolutePath);
        Assert.Equal("?pageNumber=2&pageSize=10", requested.Query);
        Assert.Equal(24, result.Value!.TotalCount);
    }

    [Fact]
    public async Task RecordReeferReadingAsync_reads_the_opened_excursion()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Json(HttpStatusCode.Created, RecordedReeferReadingJson));

        var result = await CreateClient(httpClient).RecordReeferReadingAsync(
            ControlContainerId,
            new RecordReeferReadingWebRequest(6.5m, OccurredUtc, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Reading.IsWithinThresholds);
        Assert.NotNull(result.Value.Excursion);
        Assert.Equal("Above", result.Value.Excursion!.Direction);
    }

    [Fact]
    public async Task ResolveReeferExcursionAsync_surfaces_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """
            {"title":"Temperature excursion is no longer open.","detail":"Reload and try again."}
            """));

        var result = await CreateClient(httpClient).ResolveReeferExcursionAsync(
            ControlContainerId,
            ReeferExcursionId,
            new ResolveReeferExcursionWebRequest("CorrectiveAction", "Unit restarted"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Temperature excursion is no longer open.", result.Failure.Title);
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
