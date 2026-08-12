using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HarborOps.Web.Api;

internal sealed class BearerTokenHandler(
    IHttpContextAccessor httpContextAccessor,
    IdentitySessionApiClient identitySessionApiClient) : DelegatingHandler
{
    private const string AccessTokenName = "access_token";
    private const string RefreshTokenName = "refresh_token";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        var ticket = context is null
            ? null
            : await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var accessToken = ticket?.Properties?.GetTokenValue(AccessTokenName);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || ticket is null || context is null)
        {
            return response;
        }

        var refreshToken = ticket.Properties?.GetTokenValue(RefreshTokenName);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return response;
        }

        var refreshed = await identitySessionApiClient.RefreshAsync(refreshToken, cancellationToken);
        if (refreshed is null)
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return response;
        }

        response.Dispose();
        ticket.Properties!.StoreTokens([
            new AuthenticationToken { Name = AccessTokenName, Value = refreshed.AccessToken },
            new AuthenticationToken { Name = RefreshTokenName, Value = refreshed.RefreshToken },
        ]);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ticket.Principal!, ticket.Properties);

        var retry = await CloneAsync(request, cancellationToken);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var content = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(content);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
