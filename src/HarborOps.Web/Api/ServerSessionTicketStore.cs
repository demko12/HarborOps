using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace HarborOps.Web.Api;

/// <summary>Keeps OAuth-style tokens server-side; the browser cookie contains only a protected key.</summary>
internal sealed class ServerSessionTicketStore(IMemoryCache cache) : ITicketStore
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var key = $"harborops-session-{Guid.NewGuid():N}";
        cache.Set(key, ticket, SessionLifetime);
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        cache.Set(key, ticket, SessionLifetime);
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        cache.TryGetValue(key, out AuthenticationTicket? ticket);
        return Task.FromResult(ticket);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cache.Remove(key);
        return Task.CompletedTask;
    }
}
