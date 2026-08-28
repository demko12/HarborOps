using System.Security.Claims;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace HarborOps.Modules.Identity.Application;

internal sealed class AccountSecurityService(
    UserManager<IdentityUser<Guid>> userManager,
    ILogger<AccountSecurityService> logger)
{
    public async Task ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordRequest request)
    {
        var user = await GetActiveCurrentUserAsync(principal);
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            throw new AccountSecurityValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.CurrentPassword)] = ["Current password is required."],
                [nameof(request.NewPassword)] = ["New password is required."],
            });
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            logger.LogInformation("Identity self-service password change rejected. UserId: {UserId}; Result: ValidationFailed", user.Id);
            var passwordMismatch = result.Errors.Any(error => string.Equals(error.Code, "PasswordMismatch", StringComparison.Ordinal));
            throw new AccountSecurityValidationException(passwordMismatch
                ? new Dictionary<string, string[]> { [nameof(request.CurrentPassword)] = ["The current password is incorrect."] }
                : new Dictionary<string, string[]> { [nameof(request.NewPassword)] = result.Errors.Select(error => error.Description).ToArray() });
        }

        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
        logger.LogInformation("Identity self-service password changed. UserId: {UserId}; Result: Succeeded", user.Id);
    }

    public async Task RevokeSessionsAsync(ClaimsPrincipal principal)
    {
        var user = await GetActiveCurrentUserAsync(principal);
        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
        logger.LogInformation("Identity self-service sessions revoked. UserId: {UserId}; Result: Succeeded", user.Id);
    }

    private async Task<IdentityUser<Guid>> GetActiveCurrentUserAsync(ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal) ?? throw new AccountSecurityUnauthorizedException();
        if (user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            throw new AccountSecurityUnauthorizedException();
        }

        var tokenSecurityStamp = principal.FindFirstValue(userManager.Options.ClaimsIdentity.SecurityStampClaimType);
        if (string.IsNullOrWhiteSpace(tokenSecurityStamp) || !string.Equals(tokenSecurityStamp, user.SecurityStamp, StringComparison.Ordinal))
        {
            throw new AccountSecurityUnauthorizedException();
        }

        return user;
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("The account security operation could not be completed.");
        }
    }
}

internal sealed class AccountSecurityValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class AccountSecurityUnauthorizedException : Exception;
