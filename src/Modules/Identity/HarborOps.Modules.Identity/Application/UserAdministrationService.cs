using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HarborOps.Modules.Identity.Application;

internal sealed class UserAdministrationService(
    UserManager<IdentityUser<Guid>> userManager,
    IdentityAdministratorGuard administratorGuard,
    ILogger<UserAdministrationService> logger)
{
    public async Task<SearchUsersResponse> SearchAsync(
        SearchUsersRequest request,
        CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber ?? 1;
        var pageSize = request.PageSize ?? 25;

        var errors = new Dictionary<string, string[]>();
        if (pageNumber < 1)
        {
            errors[nameof(request.PageNumber)] = ["Page number must be at least 1."];
        }
        if (pageSize is < 1 or > 200)
        {
            errors[nameof(request.PageSize)] = ["Page size must be between 1 and 200."];
        }
        if (errors.Count > 0)
        {
            throw new AdminValidationException(errors);
        }

        var query = userManager.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            query = query.Where(user => user.Email!.Contains(request.Search));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageOfUsers = await query
            .OrderBy(user => user.Email)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = new List<UserSummaryResponse>(pageOfUsers.Count);
        foreach (var user in pageOfUsers)
        {
            var roles = await userManager.GetRolesAsync(user);
            var lockoutEnd = user.LockoutEnd;
            items.Add(new UserSummaryResponse(
                user.Id,
                user.Email!,
                roles.ToArray(),
                lockoutEnd is null || lockoutEnd <= DateTimeOffset.UtcNow,
                lockoutEnd));
        }

        return new SearchUsersResponse(items, pageNumber, pageSize, totalCount);
    }

    public async Task<CreateUserResponse> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors[nameof(request.Email)] = ["Email is required."];
        }
        if (string.IsNullOrWhiteSpace(request.TemporaryPassword))
        {
            errors[nameof(request.TemporaryPassword)] = ["Temporary password is required."];
        }
        if (errors.Count > 0)
        {
            throw new AdminValidationException(errors);
        }

        if (await userManager.FindByEmailAsync(request.Email) is not null)
        {
            throw new DuplicateUserEmailException(request.Email);
        }

        var user = new IdentityUser<Guid>
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, request.TemporaryPassword);
        if (!result.Succeeded)
        {
            throw new AdminValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.TemporaryPassword)] = result.Errors
                    .Select(error => error.Description)
                    .ToArray(),
            });
        }

        return new CreateUserResponse(user.Id, user.Email!);
    }

    public async Task DisableAsync(Guid userId, Guid actingAdministratorId, CancellationToken cancellationToken)
    {
        if (userId == actingAdministratorId)
        {
            throw new SelfAccountLifecycleException();
        }

        var user = await FindUserAsync(userId);
        await administratorGuard.EnsureFinalAdministratorPreservedForUserDisableAsync(user, cancellationToken);
        EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue));
        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
        logger.LogInformation("Identity account disabled. TargetUserId: {TargetUserId}; ActingAdministratorId: {ActingAdministratorId}", userId, actingAdministratorId);
    }

    public async Task ReactivateAsync(Guid userId, Guid actingAdministratorId, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userId);
        EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, null));
        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
        logger.LogInformation("Identity account reactivated. TargetUserId: {TargetUserId}; ActingAdministratorId: {ActingAdministratorId}", userId, actingAdministratorId);
    }

    public async Task SetTemporaryPasswordAsync(
        Guid userId,
        Guid actingAdministratorId,
        SetTemporaryPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TemporaryPassword))
        {
            throw new AdminValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.TemporaryPassword)] = ["Temporary password is required."],
            });
        }

        var user = await FindUserAsync(userId);
        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, resetToken, request.TemporaryPassword);
        if (!result.Succeeded)
        {
            throw new AdminValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.TemporaryPassword)] = result.Errors.Select(error => error.Description).ToArray(),
            });
        }

        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
        logger.LogInformation("Identity account temporary password assigned. TargetUserId: {TargetUserId}; ActingAdministratorId: {ActingAdministratorId}", userId, actingAdministratorId);
    }

    public async Task RevokeSessionsAsync(Guid userId, Guid actingAdministratorId, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userId);
        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
        logger.LogInformation("Identity account sessions revoked. TargetUserId: {TargetUserId}; ActingAdministratorId: {ActingAdministratorId}", userId, actingAdministratorId);
    }

    private async Task<IdentityUser<Guid>> FindUserAsync(Guid userId) =>
        await userManager.FindByIdAsync(userId.ToString()) ?? throw new UserNotFoundException(userId);

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("The account lifecycle operation could not be completed.");
        }
    }
}
