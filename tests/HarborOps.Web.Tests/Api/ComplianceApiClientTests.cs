using System.Net;
using System.Text;
using HarborOps.Web.Api.Compliance;

namespace HarborOps.Web.Tests.Api;

public sealed class ComplianceApiClientTests
{
    [Fact]
    public async Task Client_uses_the_actual_overview_and_write_endpoints()
    {
        var requests = new List<(HttpMethod Method, string Path)>();
        using var http = CreateClient((request, _) =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            var body = request.Method == HttpMethod.Get
                ? EmptyOverview
                : request.RequestUri!.AbsolutePath.EndsWith("/certificates", StringComparison.Ordinal)
                    ? "{\"id\":\"30000000-0000-0000-0000-000000000003\"}"
                    : "{\"id\":1}";
            return Json(request.Method == HttpMethod.Get ? HttpStatusCode.OK : HttpStatusCode.Created, body);
        });
        var client = new ComplianceApiClient(http);
        var vesselId = Guid.NewGuid();

        await client.GetOverviewAsync(vesselId, CancellationToken.None);
        var certificate = await client.RegisterCertificateAsync(new(vesselId, "Safety", "1", "Authority", new(2026, 1, 1), new(2027, 1, 1)), CancellationToken.None);
        var inspection = await client.RegisterPscInspectionAsync(new(vesselId, Guid.NewGuid(), new(2026, 8, 1), "Authority", 0, false), CancellationToken.None);

        Assert.Contains((HttpMethod.Get, $"/api/compliance/vessels/{vesselId}/overview"), requests);
        Assert.Contains((HttpMethod.Post, "/api/compliance/certificates"), requests);
        Assert.Contains((HttpMethod.Post, "/api/compliance/psc-inspections"), requests);
        Assert.Equal(Guid.Parse("30000000-0000-0000-0000-000000000003"), certificate.Value!.Id);
        Assert.Equal(1, inspection.Value!.Id);
    }

    [Fact]
    public async Task Client_maps_server_validation_errors()
    {
        using var http = CreateClient((_, _) => Json(HttpStatusCode.BadRequest,
            "{\"title\":\"Validation failed\",\"errors\":{\"certificateNumber\":[\"Required\"]}}"));
        var result = await new ComplianceApiClient(http).RegisterCertificateAsync(
            new(Guid.NewGuid(), null, null, null, null, null), CancellationToken.None);
        Assert.True(result.Failure!.IsValidation);
        Assert.Equal("Required", Assert.Single(result.Failure.Errors["certificateNumber"]));
    }

    private const string EmptyOverview = "{\"certificates\":[],\"pscInspections\":[],\"openNonConformities\":[]}";
    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
        new(new Handler(handler)) { BaseAddress = new Uri("https://localhost/") };
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(handler(request, token)); }
}
