using System.Security.Claims;
using HarborOps.Web.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace HarborOps.Web.Tests.Api;

public sealed class ServerSessionTicketStoreTests
{
    [Fact]
    public async Task RevokeUserSessionsAsync_removes_only_the_target_users_server_side_tickets()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new ServerSessionTicketStore(cache);
        var revokedUserId = Guid.NewGuid();
        var unaffectedUserId = Guid.NewGuid();
        var revokedTicketKey = await store.StoreAsync(CreateTicket(revokedUserId), CancellationToken.None);
        var unaffectedTicketKey = await store.StoreAsync(CreateTicket(unaffectedUserId), CancellationToken.None);

        await store.RevokeUserSessionsAsync(revokedUserId);

        Assert.Null(await store.RetrieveAsync(revokedTicketKey, CancellationToken.None));
        Assert.NotNull(await store.RetrieveAsync(unaffectedTicketKey, CancellationToken.None));
    }

    private static AuthenticationTicket CreateTicket(Guid userId)
    {
        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        return new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties(),
            CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
