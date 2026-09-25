using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Identity.Application;

internal sealed class DocumentRecipientLookup(UserManager<IdentityUser<Guid>> userManager, RoleManager<IdentityRole<Guid>> roleManager)
    : IDocumentRecipientLookup
{
    public async Task<IReadOnlyList<DocumentRecipientAccount>> LookupAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0) return [];
        var distinctIds = userIds.Distinct().Take(100).ToArray();
        var users = await userManager.Users.AsNoTracking().Where(x => distinctIds.Contains(x.Id)).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var result = new List<DocumentRecipientAccount>(users.Count);
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var canRead = false;
            foreach (var roleName in roles)
            {
                var role = await roleManager.FindByNameAsync(roleName);
                if (role is null) continue;
                var claims = await roleManager.GetClaimsAsync(role);
                if (claims.Any(x => x.Type == Permissions.ClaimType && x.Value == Permissions.Documents.Read)) { canRead = true; break; }
            }
            result.Add(new DocumentRecipientAccount(user.Id, user.Email ?? string.Empty, user.LockoutEnd is null || user.LockoutEnd <= now, canRead));
        }
        return result;
    }

    public async Task<IReadOnlyList<DocumentRecipientAccount>> SearchAsync(string? search, int take, CancellationToken cancellationToken)
    {
        var ids = await userManager.Users.AsNoTracking()
            .Where(x => string.IsNullOrWhiteSpace(search) || x.Email!.Contains(search))
            .OrderBy(x => x.Email).Select(x => x.Id).Take(Math.Clamp(take, 1, 100)).ToListAsync(cancellationToken);
        return await LookupAsync(ids, cancellationToken);
    }
}
