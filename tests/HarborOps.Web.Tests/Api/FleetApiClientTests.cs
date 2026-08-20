using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api.Fleet;

namespace HarborOps.Web.Tests.Api;

public sealed class FleetApiClientTests
{
    [Fact]
    public async Task GetVesselsAsync_sends_search_status_and_paging_to_fleet_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[],"pageNumber":2,"pageSize":10,"totalCount":0}""");
        });

        var result = await new FleetApiClient(httpClient).GetVesselsAsync(
            "Northern Star", "Active", 2, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/fleet/vessels", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("search=Northern%20Star", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("status=Active", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVesselsAsync_returns_retryable_failure_when_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException());

        var result = await new FleetApiClient(httpClient).GetVesselsAsync(null, null, 1, 25, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task GetVesselsAsync_returns_clear_message_when_fleet_access_is_forbidden()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, string.Empty));

        var result = await new FleetApiClient(httpClient).GetVesselsAsync(null, null, 1, 25, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Fleet access denied", result.Failure!.Title);
    }

    [Fact]
    public async Task GetVesselsAsync_surfaces_validation_errors_from_problem_details()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"status":["Status is not a known vessel status."]}}"""));

        var result = await new FleetApiClient(httpClient).GetVesselsAsync(
            null, "Sailing", 1, 25, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Equal("One or more validation errors occurred.", result.Failure.Title);
        Assert.Equal(
            "Status is not a known vessel status.",
            Assert.Single(result.Failure.Errors["status"]));
    }

    [Fact]
    public async Task GetVesselsAsync_reports_no_validation_errors_for_other_failures()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, string.Empty));

        var result = await new FleetApiClient(httpClient).GetVesselsAsync(null, null, 1, 25, CancellationToken.None);

        Assert.False(result.Failure!.IsValidation);
        Assert.Empty(result.Failure.Errors);
    }

    [Fact]
    public async Task LookupVesselNamesAsync_returns_an_id_to_name_map()
    {
        var vesselId = Guid.NewGuid();
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.OK,
            $$"""{"vessels":[{"id":"{{vesselId}}","name":"Aurora"}]}"""));

        var result = await new FleetApiClient(httpClient)
            .LookupVesselNamesAsync([vesselId], CancellationToken.None);

        Assert.Equal("Aurora", result[vesselId]);
    }

    [Fact]
    public async Task LookupVesselNamesAsync_returns_an_empty_map_without_calling_the_api_for_no_ids()
    {
        var called = false;
        using var httpClient = CreateHttpClient((_, _) =>
        {
            called = true;
            return Json(HttpStatusCode.OK, """{"vessels":[]}""");
        });

        var result = await new FleetApiClient(httpClient)
            .LookupVesselNamesAsync([], CancellationToken.None);

        Assert.Empty(result);
        Assert.False(called);
    }

    [Fact]
    public async Task LookupVesselNamesAsync_returns_an_empty_map_when_the_api_is_unreachable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await new FleetApiClient(httpClient)
            .LookupVesselNamesAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task LookupVesselNamesAsync_returns_an_empty_map_for_a_non_success_response()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.BadRequest, "{}"));

        var result = await new FleetApiClient(httpClient)
            .LookupVesselNamesAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.Empty(result);
    }

    private static readonly Guid VesselId = Guid.Parse("40000000-0000-0000-0000-000000000009");

    [Fact]
    public async Task GetVesselDetailsAsync_asks_for_the_vessel_by_its_identifier()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, DetailsJson);
        });

        var result = await new FleetApiClient(httpClient).GetVesselDetailsAsync(VesselId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal($"/api/fleet/vessels/{VesselId}", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetVesselDetailsAsync_projects_the_vessel_and_its_history()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, DetailsJson));

        var result = await new FleetApiClient(httpClient).GetVesselDetailsAsync(VesselId, CancellationToken.None);

        var details = result.Value!;

        Assert.Equal(VesselId, details.VesselId);
        Assert.Equal("Northern Star", details.Name);
        Assert.Equal("5100001", details.ImoNumber);
        Assert.Equal("Bulk carrier", details.VesselTypeName);
        Assert.Equal("Active", details.CurrentStatus);
        Assert.Equal(52000m, details.Dwt);
        Assert.Equal("Adriatic Fleet", details.FleetName);

        var entry = Assert.Single(details.StatusHistory);

        Assert.Equal("Active", entry.Status);
        Assert.Equal("operator@harborops.test", entry.ChangedBy);
        Assert.Equal("Returned to service", entry.Notes);
    }

    [Fact]
    public async Task GetVesselDetailsAsync_keeps_an_unassigned_vessel_without_a_fleet()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.OK,
            DetailsJson.Replace(
                "\"fleetId\":\"70000000-0000-0000-0000-000000000001\",\"fleetName\":\"Adriatic Fleet\"",
                "\"fleetId\":null,\"fleetName\":null",
                StringComparison.Ordinal)));

        var result = await new FleetApiClient(httpClient).GetVesselDetailsAsync(
            VesselId,
            CancellationToken.None);

        Assert.Null(result.Value!.FleetId);
        Assert.Null(result.Value.FleetName);
    }

    [Fact]
    public async Task GetVesselDetailsAsync_reports_a_missing_vessel_as_not_found()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.NotFound, string.Empty));

        var result = await new FleetApiClient(httpClient).GetVesselDetailsAsync(VesselId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
        Assert.Equal("Vessel not found", result.Failure.Title);
        Assert.Equal("This vessel does not exist or may have been removed.", result.Failure.Message);
    }

    [Fact]
    public async Task GetVesselDetailsAsync_returns_clear_message_when_the_read_is_forbidden()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, string.Empty));

        var result = await new FleetApiClient(httpClient).GetVesselDetailsAsync(VesselId, CancellationToken.None);

        Assert.True(result.Failure!.IsForbidden);
        Assert.Equal("Fleet access denied", result.Failure.Title);
        Assert.Equal("Your account does not have permission to view this vessel.", result.Failure.Message);
    }

    [Fact]
    public async Task GetVesselDetailsAsync_prefers_the_problem_details_over_its_own_wording()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.NotFound,
            """{"title":"Vessel not found.","detail":"Vessel 'abc' was not found.","status":404}"""));

        var result = await new FleetApiClient(httpClient).GetVesselDetailsAsync(VesselId, CancellationToken.None);

        Assert.Equal("Vessel not found.", result.Failure!.Title);
        Assert.Equal("Vessel 'abc' was not found.", result.Failure.Message);
    }

    [Fact]
    public async Task GetVesselDetailsAsync_returns_retryable_failure_when_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException());

        var result = await new FleetApiClient(httpClient).GetVesselDetailsAsync(VesselId, CancellationToken.None);

        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task GetVesselDetailsAsync_lets_a_cancelled_read_surface()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = CreateHttpClient((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Json(HttpStatusCode.OK, DetailsJson);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new FleetApiClient(httpClient).GetVesselDetailsAsync(VesselId, cancellation.Token));
    }

    [Fact]
    public async Task RecordStatusChangeAsync_posts_the_change_to_the_vessel()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, _) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(CancellationToken.None);
            return await Json(HttpStatusCode.Created, StatusChangeJson);
        });

        var result = await new FleetApiClient(httpClient).RecordStatusChangeAsync(
            VesselId,
            new RecordVesselStatusChangeWebRequest("At Port", "Berthed overnight"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/fleet/vessels/{VesselId}/status-changes", captured.RequestUri!.AbsolutePath);
        Assert.Contains("\"status\":\"At Port\"", body, StringComparison.Ordinal);
        Assert.Contains("\"notes\":\"Berthed overnight\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordStatusChangeAsync_never_sends_the_author_of_the_change()
    {
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, _) =>
        {
            body = await request.Content!.ReadAsStringAsync(CancellationToken.None);
            return await Json(HttpStatusCode.Created, StatusChangeJson);
        });

        await new FleetApiClient(httpClient).RecordStatusChangeAsync(
            VesselId,
            new RecordVesselStatusChangeWebRequest("At Port", null),
            CancellationToken.None);

        Assert.DoesNotContain("changedBy", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vesselId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecordStatusChangeAsync_returns_the_recorded_change()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Created, StatusChangeJson));

        var result = await new FleetApiClient(httpClient).RecordStatusChangeAsync(
            VesselId,
            new RecordVesselStatusChangeWebRequest("At Port", null),
            CancellationToken.None);

        var recorded = result.Value!;

        Assert.Equal(VesselId, recorded.VesselId);
        Assert.Equal("At Port", recorded.Status);
        Assert.Equal("operator@harborops.test", recorded.ChangedBy);
    }

    [Fact]
    public async Task RecordStatusChangeAsync_surfaces_validation_errors_from_problem_details()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"status":["Status is required."]}}"""));

        var result = await new FleetApiClient(httpClient).RecordStatusChangeAsync(
            VesselId,
            new RecordVesselStatusChangeWebRequest(null, null),
            CancellationToken.None);

        Assert.True(result.Failure!.IsValidation);
        Assert.Equal("Status is required.", Assert.Single(result.Failure.Errors["status"]));
    }

    [Fact]
    public async Task RecordStatusChangeAsync_reports_an_unchanged_status_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Vessel status is unchanged.","detail":"The vessel already has this status.","status":409}"""));

        var result = await new FleetApiClient(httpClient).RecordStatusChangeAsync(
            VesselId,
            new RecordVesselStatusChangeWebRequest("Active", null),
            CancellationToken.None);

        Assert.True(result.Failure!.IsConflict);
        Assert.False(result.Failure.IsValidation);
        Assert.Equal("Vessel status is unchanged.", result.Failure.Title);
    }

    [Fact]
    public async Task RecordStatusChangeAsync_returns_clear_message_when_the_change_is_forbidden()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, string.Empty));

        var result = await new FleetApiClient(httpClient).RecordStatusChangeAsync(
            VesselId,
            new RecordVesselStatusChangeWebRequest("At Port", null),
            CancellationToken.None);

        Assert.True(result.Failure!.IsForbidden);
        Assert.Equal("Your account does not have permission to change vessel status.", result.Failure.Message);
    }

    [Fact]
    public async Task RecordStatusChangeAsync_reports_a_missing_vessel_as_not_found()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.NotFound, string.Empty));

        var result = await new FleetApiClient(httpClient).RecordStatusChangeAsync(
            VesselId,
            new RecordVesselStatusChangeWebRequest("At Port", null),
            CancellationToken.None);

        Assert.True(result.Failure!.IsNotFound);
        Assert.Equal("Vessel not found", result.Failure.Title);
    }

    [Fact]
    public async Task RecordStatusChangeAsync_returns_retryable_failure_when_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException());

        var result = await new FleetApiClient(httpClient).RecordStatusChangeAsync(
            VesselId,
            new RecordVesselStatusChangeWebRequest("At Port", null),
            CancellationToken.None);

        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task RecordStatusChangeAsync_reports_no_validation_errors_for_other_failures()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Conflict, string.Empty));

        var result = await new FleetApiClient(httpClient).RecordStatusChangeAsync(
            VesselId,
            new RecordVesselStatusChangeWebRequest("Active", null),
            CancellationToken.None);

        Assert.False(result.Failure!.IsValidation);
        Assert.Empty(result.Failure.Errors);
    }

    private const string DetailsJson = """
        {"vesselId":"40000000-0000-0000-0000-000000000009","companyId":"60000000-0000-0000-0000-000000000001",
        "name":"Northern Star","imoNumber":"5100001","mmsi":"256123000","callSign":"9HA1234",
        "flagState":"Montenegro","vesselTypeId":"50000000-0000-0000-0000-000000000001",
        "vesselTypeName":"Bulk carrier","dwt":52000,"gt":29000,"loa":190,"beam":32,"draft":12,
        "portOfRegistry":"Bar","classificationSociety":"Lloyd's Register","lifecycleStage":"In service",
        "currentStatus":"Active","createdAtUtc":"2026-01-01T08:00:00Z","updatedAtUtc":"2026-08-01T10:00:00Z",
        "fleetId":"70000000-0000-0000-0000-000000000001","fleetName":"Adriatic Fleet",
        "statusHistory":[{"id":"80000001-0000-0000-0000-000000000001","status":"Active",
        "changedBy":"operator@harborops.test","notes":"Returned to service","changedAtUtc":"2026-08-01T10:00:00Z"}]}
        """;

    private const string StatusChangeJson = """
        {"id":"90000000-0000-0000-0000-000000000001","vesselId":"40000000-0000-0000-0000-000000000009",
        "status":"At Port","changedBy":"operator@harborops.test","notes":null,
        "changedAtUtc":"2026-08-04T10:00:00Z"}
        """;

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory)) { BaseAddress = new Uri("https://api.harborops.test/") };

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
