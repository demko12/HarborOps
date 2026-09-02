using System.Net;
using System.Text;
using HarborOps.Web.Api;

namespace HarborOps.Web.Tests.Api;

public sealed class NotificationsApiClientTests
{
    [Fact]
    public async Task Client_gets_pages_and_posts_acknowledgements_to_the_expected_endpoints()
    {
        var requests = new List<(HttpMethod Method, string PathAndQuery)>();
        var notificationId = Guid.NewGuid();
        using var httpClient = new HttpClient(new Handler((request, _) =>
        {
            requests.Add((request.Method, request.RequestUri!.PathAndQuery));
            return request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("{\"items\":[],\"pageNumber\":2,\"pageSize\":8,\"totalCount\":0,\"unreadCount\":0}") }
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        }))
        { BaseAddress = new Uri("https://api.harborops.test/") };
        var client = new NotificationsApiClient(httpClient);

        var page = await client.GetAsync("unread", 2, 8, CancellationToken.None);
        await client.AcknowledgeAsync(notificationId, CancellationToken.None);
        await client.AcknowledgeAllAsync(CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal(2, page.PageNumber);
        Assert.Contains((HttpMethod.Get, "/api/notifications?status=unread&pageNumber=2&pageSize=8"), requests);
        Assert.Contains((HttpMethod.Post, $"/api/notifications/{notificationId}/acknowledge"), requests);
        Assert.Contains((HttpMethod.Post, "/api/notifications/acknowledge-all"), requests);
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request, cancellationToken));
    }
}
