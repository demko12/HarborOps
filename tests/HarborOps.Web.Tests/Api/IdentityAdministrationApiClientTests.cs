using System.Net;
using System.Text;
using HarborOps.Web.Api;

namespace HarborOps.Web.Tests.Api;

public sealed class IdentityAdministrationApiClientTests
{
    [Fact]
    public async Task SearchUsersAsync_sends_paging_and_search_to_administration_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[],"pageNumber":2,"pageSize":10,"totalCount":0}""");
        });

        var result = await CreateClient(httpClient).SearchUsersAsync("captain@example.test", 2, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/identity/admin/users", captured.RequestUri!.AbsolutePath);
        Assert.Contains("search=captain%40example.test", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Equal(2, result.Value!.PageNumber);
    }

    [Fact]
    public async Task CreateUserAsync_sends_only_email_and_temporary_password_and_does_not_log_them()
    {
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, _) =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return await Json(HttpStatusCode.Created, """{"id":"10000000-0000-0000-0000-000000000000","email":"new@example.test"}""");
        });

        var result = await CreateClient(httpClient).CreateUserAsync(new("new@example.test", "Temporary!42"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("temporaryPassword", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirmation", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "Final administrator protection.", true, false)]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden", false, true)]
    public async Task ReplaceUserRolesAsync_returns_authoritative_api_failure(
        HttpStatusCode statusCode,
        string title,
        bool expectedConflict,
        bool expectedForbidden)
    {
        using var httpClient = CreateHttpClient((_, _) => Json(statusCode, $$"""{"title":"{{title}}","detail":"The edit was not applied."}"""));

        var result = await CreateClient(httpClient).ReplaceUserRolesAsync(Guid.NewGuid(), new(["Administrator"]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedConflict, result.Failure!.IsConflict);
        Assert.Equal(expectedForbidden, result.Failure.IsForbidden);
        Assert.Equal("The edit was not applied.", result.Failure.Message);
    }

    [Fact]
    public async Task SetTemporaryPasswordAsync_uses_the_lifecycle_endpoint_without_exposing_a_confirmation_value()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient(async (request, cancellationToken) =>
        {
            captured = request;
            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("temporaryPassword", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("confirmation", content, StringComparison.OrdinalIgnoreCase);
            return await Json(HttpStatusCode.NoContent, string.Empty);
        });
        var userId = Guid.NewGuid();

        var result = await CreateClient(httpClient).SetTemporaryPasswordAsync(
            userId,
            new SetAdminTemporaryPasswordRequest("Temporary!42"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/identity/admin/users/{userId}/temporary-password", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RevokeUserSessionsAsync_uses_the_account_specific_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.NoContent, string.Empty);
        });
        var userId = Guid.NewGuid();

        var result = await CreateClient(httpClient).RevokeUserSessionsAsync(userId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"/api/identity/admin/users/{userId}/revoke-sessions", captured!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ListRolesAsync_returns_retryable_failure_when_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await CreateClient(httpClient).ListRolesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    private static IdentityAdministrationApiClient CreateClient(HttpClient httpClient) => new(httpClient);

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory)) { BaseAddress = new Uri("https://api.harborops.test/") };

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/problem+json") });

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
