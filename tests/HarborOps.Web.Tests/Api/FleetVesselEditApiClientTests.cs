using System.Net;
using System.Text;
using HarborOps.Web.Api.Fleet;

namespace HarborOps.Web.Tests.Api;

public sealed class FleetVesselEditApiClientTests
{
    private static readonly Guid VesselId = Guid.Parse("40000000-0000-0000-0000-000000000009");

    [Fact]
    public async Task GetVesselTypesAsync_reads_the_vessel_type_catalogue()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """
                {"vesselTypes":[{"id":"50000000-0000-0000-0000-000000000001","name":"Bulk Carrier","code":"BULK"},
                {"id":"50000000-0000-0000-0000-000000000002","name":"Container Ship","code":"CON"}]}
                """);
        });

        var result = await new FleetApiClient(httpClient).GetVesselTypesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/fleet/vessel-types", captured.RequestUri!.AbsolutePath);
        Assert.Equal(2, result.Value!.VesselTypes.Count);
        Assert.Equal("Bulk Carrier", result.Value.VesselTypes[0].Name);
    }

    [Fact]
    public async Task UpdateVesselRegistryDetailsAsync_puts_the_editable_fields()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, _) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(CancellationToken.None);
            return await Json(HttpStatusCode.OK, DetailsJson);
        });

        var result = await new FleetApiClient(httpClient).UpdateVesselRegistryDetailsAsync(
            VesselId,
            new UpdateVesselRegistryDetailsWebRequest(
                Guid.Parse("50000000-0000-0000-0000-000000000002"),
                "Northern Star",
                "256123000",
                "9HA1234",
                "Montenegro",
                "Bar",
                "Lloyd's Register",
                "In service",
                "MSMD-42"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.Equal($"/api/fleet/vessels/{VesselId}/registry-details", captured.RequestUri!.AbsolutePath);
        Assert.Contains("\"name\":\"Northern Star\"", body, StringComparison.Ordinal);
        Assert.Contains("\"mmsi\":\"256123000\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("imoNumber", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("companyId", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Northern Star", result.Value!.Name);
    }

    [Fact]
    public async Task UpdateVesselRegistryDetailsAsync_reports_a_duplicate_identifier_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"A vessel with this MMSI already exists.","detail":"A vessel with MMSI '256123000' already exists.","status":409}"""));

        var result = await new FleetApiClient(httpClient).UpdateVesselRegistryDetailsAsync(
            VesselId,
            RegistryRequest(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("A vessel with this MMSI already exists.", result.Failure.Title);
    }

    [Fact]
    public async Task UpdateVesselRegistryDetailsAsync_surfaces_validation_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"mmsi":["MMSI must contain digits only."]}}"""));

        var result = await new FleetApiClient(httpClient).UpdateVesselRegistryDetailsAsync(
            VesselId,
            RegistryRequest(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsValidation);
        Assert.Equal("MMSI must contain digits only.", Assert.Single(result.Failure.Errors["mmsi"]));
    }

    [Fact]
    public async Task UpdateVesselRegistryDetailsAsync_reports_a_forbidden_edit()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, string.Empty));

        var result = await new FleetApiClient(httpClient).UpdateVesselRegistryDetailsAsync(
            VesselId,
            RegistryRequest(),
            CancellationToken.None);

        Assert.True(result.Failure!.IsForbidden);
    }

    [Fact]
    public async Task UpdateVesselSpecificationsAsync_puts_the_measurements()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, _) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(CancellationToken.None);
            return await Json(HttpStatusCode.OK, DetailsJson);
        });

        var result = await new FleetApiClient(httpClient).UpdateVesselSpecificationsAsync(
            VesselId,
            new UpdateVesselSpecificationsWebRequest(52000m, 29000m, 190m, 32m, 12m),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.Equal($"/api/fleet/vessels/{VesselId}/specifications", captured.RequestUri!.AbsolutePath);
        Assert.Contains("\"dwt\":52000", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateVesselSpecificationsAsync_returns_retryable_failure_when_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException());

        var result = await new FleetApiClient(httpClient).UpdateVesselSpecificationsAsync(
            VesselId,
            new UpdateVesselSpecificationsWebRequest(52000m, null, null, null, null),
            CancellationToken.None);

        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    private static UpdateVesselRegistryDetailsWebRequest RegistryRequest() => new(
        Guid.Parse("50000000-0000-0000-0000-000000000002"),
        "Northern Star",
        "256123000",
        "9HA1234",
        "Montenegro",
        null,
        null,
        null,
        null);

    private const string DetailsJson = """
        {"vesselId":"40000000-0000-0000-0000-000000000009","companyId":"60000000-0000-0000-0000-000000000001",
        "name":"Northern Star","imoNumber":"5100001","mmsi":"256123000","callSign":"9HA1234",
        "flagState":"Montenegro","vesselTypeId":"50000000-0000-0000-0000-000000000002",
        "vesselTypeName":"Container Ship","dwt":52000,"gt":29000,"loa":190,"beam":32,"draft":12,
        "portOfRegistry":"Bar","classificationSociety":"Lloyd's Register","lifecycleStage":"In service",
        "msmdId":"MSMD-42","currentStatus":"Active","createdAtUtc":"2026-01-01T08:00:00Z",
        "updatedAtUtc":"2026-08-31T10:00:00Z","fleetId":null,"fleetName":null,"statusHistory":[]}
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
