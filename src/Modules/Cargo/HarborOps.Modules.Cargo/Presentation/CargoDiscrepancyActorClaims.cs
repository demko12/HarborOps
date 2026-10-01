using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using HarborOps.Modules.Cargo.Contracts;

namespace HarborOps.Modules.Cargo.Presentation;

internal static class CargoDiscrepancyActorClaims
{
    internal static bool TryRead(ClaimsPrincipal user, [NotNullWhen(true)] out CargoDiscrepancyActor? actor)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            actor = null;

            return false;
        }

        var name = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue(ClaimTypes.Name);

        actor = new CargoDiscrepancyActor(userId, string.IsNullOrWhiteSpace(name) ? null : name.Trim());

        return true;
    }
}
