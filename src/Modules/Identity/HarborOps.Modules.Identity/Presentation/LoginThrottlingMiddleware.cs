using System.Text.Json;
using HarborOps.Modules.Identity.Application;
using Microsoft.AspNetCore.Http;

namespace HarborOps.Modules.Identity.Presentation;

public sealed class LoginThrottlingMiddleware(RequestDelegate next)
{
    private const string LoginPath = "/api/identity/login";

    public async Task InvokeAsync(HttpContext context, LoginAttemptThrottle throttle)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || !context.Request.Path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var email = await ReadEmailAsync(context, context.RequestAborted);
        if (!throttle.TryBegin(email, out var lease, out var retryAfter))
        {
            await WriteThrottledResponseAsync(context, retryAfter, context.RequestAborted);
            return;
        }

        using (lease)
        {
            await next(context);
            lease?.Complete(context.Response.StatusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices);
        }
    }

    private static async Task<string?> ReadEmailAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Request.EnableBuffering();
        try
        {
            var request = await JsonSerializer.DeserializeAsync<LoginRequest>(
                context.Request.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken);
            return request?.Email;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            context.Request.Body.Position = 0;
        }
    }

    public static async Task WriteThrottledResponseAsync(HttpContext context, TimeSpan retryAfter, CancellationToken cancellationToken)
    {
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(
            new { message = "Too many sign-in attempts. Please try again shortly." },
            cancellationToken);
    }

    private sealed record LoginRequest(string? Email);
}
