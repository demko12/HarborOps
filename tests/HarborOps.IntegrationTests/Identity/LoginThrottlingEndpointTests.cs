using System.Net;
using System.Net.Http.Json;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Identity;

public sealed class LoginThrottlingEndpointTests
{
    [Fact]
    public async Task Account_limit_returns_429_for_known_and_unknown_emails_without_disclosure()
    {
        using var application = CreateThrottledApplication(clientAttemptLimit: 20);
        using var client = application.CreateClient();
        var knownEmail = $"known.{Guid.NewGuid():N}@harborops.test";
        var unknownEmail = $"unknown.{Guid.NewGuid():N}@harborops.test";
        await application.CreateTestUserAsync(knownEmail, "P@ssw0rd!2026");

        await SendFailedAttemptsAsync(client, knownEmail, 5);
        await SendFailedAttemptsAsync(client, unknownEmail, 5);

        using var knownResponse = await LoginAsync(client, knownEmail);
        using var unknownResponse = await LoginAsync(client, unknownEmail);

        Assert.Equal(HttpStatusCode.TooManyRequests, knownResponse.StatusCode);
        Assert.Equal(knownResponse.StatusCode, unknownResponse.StatusCode);
        var knownMessage = await ReadMessageAsync(knownResponse);
        var unknownMessage = await ReadMessageAsync(unknownResponse);
        Assert.Equal("Too many sign-in attempts. Please try again shortly.", knownMessage);
        Assert.Equal(knownMessage, unknownMessage);
        Assert.NotNull(knownResponse.Headers.RetryAfter);
    }

    [Fact]
    public async Task Client_ip_limit_blocks_different_accounts_after_the_configured_number_of_requests()
    {
        using var application = CreateThrottledApplication();
        using var client = application.CreateClient();

        for (var index = 0; index < 3; index++)
        {
            using var response = await LoginAsync(client, $"different.{index}.{Guid.NewGuid():N}@harborops.test");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var throttledResponse = await LoginAsync(client, $"different.final.{Guid.NewGuid():N}@harborops.test");
        Assert.Equal(HttpStatusCode.TooManyRequests, throttledResponse.StatusCode);
        Assert.NotNull(throttledResponse.Headers.RetryAfter);
    }

    private static WebApplicationFactory<Program> CreateThrottledApplication(int clientAttemptLimit = 3) =>
        new HarborOpsApiFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<LoginThrottlingOptions>(options =>
            {
                options.AccountFailedAttemptLimit = 5;
                options.ClientAttemptLimit = clientAttemptLimit;
                options.AccountFailureWindow = TimeSpan.FromMinutes(5);
                options.ClientAttemptWindow = TimeSpan.FromMinutes(1);
            })));

    private static async Task SendFailedAttemptsAsync(HttpClient client, string email, int count)
    {
        for (var index = 0; index < count; index++)
        {
            using var response = await LoginAsync(client, email);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/identity/login", new { email, password = "incorrect-password" });

    private static async Task<string?> ReadMessageAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<ThrottledResponse>();
        return payload?.Message;
    }

    private sealed record ThrottledResponse(string? Message);
}
