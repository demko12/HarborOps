using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace HarborOps.Web.Api;

/// <summary>Keeps OAuth-style tokens server-side; the browser cookie contains only a protected key.</summary>
internal sealed class ServerSessionTicketStore(IMemoryCache cache) : ITicketStore
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);
    private readonly AsyncLocal<string?> currentSessionKey = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> userSessionKeys = new();

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var key = $"harborops-session-{Guid.NewGuid():N}";
        cache.Set(key, ticket, SessionLifetime);
        IndexTicket(ticket, key);
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        cache.Set(key, ticket, SessionLifetime);
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        currentSessionKey.Value = key;
        cache.TryGetValue(key, out AuthenticationTicket? ticket);
        return Task.FromResult(ticket);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(key, out AuthenticationTicket? ticket) && ticket is not null)
        {
            RemoveTicketIndex(ticket, key);
        }

        cache.Remove(key);
        return Task.CompletedTask;
    }

    internal Task<bool> RenewCurrentAsync(AuthenticationTicket ticket)
    {
        var key = currentSessionKey.Value;
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult(false);
        }

        cache.Set(key, ticket, SessionLifetime);
        return Task.FromResult(true);
    }

    internal Task RemoveCurrentAsync()
    {
        var key = currentSessionKey.Value;
        if (!string.IsNullOrWhiteSpace(key))
        {
            return RemoveAsync(key, CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    internal Task RevokeUserSessionsAsync(Guid userId)
    {
        if (!userSessionKeys.TryRemove(userId, out var sessionKeys))
        {
            return Task.CompletedTask;
        }

        foreach (var key in sessionKeys.Keys)
        {
            cache.Remove(key);
        }

        return Task.CompletedTask;
    }

    private void IndexTicket(AuthenticationTicket ticket, string key)
    {
        if (!Guid.TryParse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return;
        }

        userSessionKeys.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>())[key] = 0;
    }

    private void RemoveTicketIndex(AuthenticationTicket ticket, string key)
    {
        if (!Guid.TryParse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !userSessionKeys.TryGetValue(userId, out var sessionKeys))
        {
            return;
        }

        sessionKeys.TryRemove(key, out _);
        if (sessionKeys.IsEmpty)
        {
            userSessionKeys.TryRemove(userId, out _);
        }
    }
}
