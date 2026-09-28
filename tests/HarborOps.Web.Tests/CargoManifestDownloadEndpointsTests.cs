using System.Net;
using System.Net.Http.Headers;
using System.Text;
using HarborOps.Web;
using HarborOps.Web.Api.Cargo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests;

public sealed class CargoManifestDownloadEndpointsTests
{
    private static readonly Guid BookingId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task The_download_forwards_the_normalized_filters_without_paging_and_returns_the_csv()
    {
        HttpRequestMessage? forwarded = null;
        var csv = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Booking number\r\n")).ToArray();
        var client = Client(request =>
        {
            forwarded = request;

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(csv) };
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv; charset=utf-8");
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "manifest-BK-1-20260928T1432Z.csv" };
            response.Headers.Add("X-Matching-Count", "0");

            return response;
        });
        var context = new DefaultHttpContext();

        var result = await CargoManifestDownloadEndpoints.DownloadManifestAsync(
            BookingId, " msku ", "Loaded", "42r1", "nlrtm", " ", context, client, CancellationToken.None);

        var file = Assert.IsType<FileContentHttpResult>(result);
        Assert.Equal(
            $"/api/cargo/bookings/{BookingId}/containers/export?search=MSKU&status=Loaded&containerType=42R1&portOfLoadingCode=NLRTM",
            forwarded!.RequestUri!.PathAndQuery);
        Assert.Equal(csv, file.FileContents.ToArray());
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Equal("manifest-BK-1-20260928T1432Z.csv", file.FileDownloadName);
        Assert.Equal("0", context.Response.Headers["X-Matching-Count"].ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"errors":{"limit":["More than 10,000 containers match."]}}""")]
    [InlineData(HttpStatusCode.Forbidden, "")]
    [InlineData(HttpStatusCode.NotFound, """{"title":"Cargo booking not found."}""")]
    public async Task An_api_failure_is_passed_through_without_a_partial_file(HttpStatusCode statusCode, string body)
    {
        var client = Client(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
        });
        var context = new DefaultHttpContext();

        var result = await CargoManifestDownloadEndpoints.DownloadManifestAsync(
            BookingId, null, null, null, null, null, context, client, CancellationToken.None);

        var content = Assert.IsType<ContentHttpResult>(result);
        Assert.Equal((int)statusCode, content.StatusCode);
        Assert.Equal(body, content.ResponseContent);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task An_unreachable_api_is_reported_as_unavailable()
    {
        var client = Client(_ => throw new HttpRequestException("down"));

        var result = await CargoManifestDownloadEndpoints.DownloadManifestAsync(
            BookingId, null, null, null, null, null, new DefaultHttpContext(), client, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal("HarborOps API is unavailable", problem.ProblemDetails.Title);
    }

    [Fact]
    public async Task A_cancelled_download_is_not_turned_into_a_file()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CargoManifestDownloadEndpoints.DownloadManifestAsync(
            BookingId, null, null, null, null, null, new DefaultHttpContext(), client, cancellation.Token));
    }

    [Fact]
    public void The_download_route_requires_cargo_access_even_on_direct_navigation()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(new CargoBookingsApiClient(new HttpClient()));
        var app = builder.Build();
        app.MapCargoManifestDownloadEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/cargo/{bookingId:guid}/manifest/export");

        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), data => data.Policy == "AccessCargo");
    }

    [Theory]
    [InlineData("/cargo/20000000-0000-0000-0000-000000000001/manifest/print", "no-store")]
    [InlineData("/CARGO/20000000-0000-0000-0000-000000000001/Manifest/Print/", "no-store")]
    [InlineData("/cargo/20000000-0000-0000-0000-000000000001", "")]
    [InlineData("/cargo/not-a-guid/manifest/print", "")]
    [InlineData("/cargo/20000000-0000-0000-0000-000000000001/manifest/print/extra", "")]
    public async Task Only_the_manifest_print_page_is_marked_as_not_cacheable(string path, string expected)
    {
        var pipeline = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        pipeline.UseNoStoreForManifestPrint();
        pipeline.Run(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await pipeline.Build()(context);

        Assert.Equal(expected, context.Response.Headers.CacheControl.ToString());
    }

    private static CargoBookingsApiClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        return new CargoBookingsApiClient(new HttpClient(new StubHttpMessageHandler(respond))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(respond(request));
        }
    }
}
