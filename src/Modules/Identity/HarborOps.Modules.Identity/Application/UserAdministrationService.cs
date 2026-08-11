using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Identity.Application;

internal sealed class UserAdministrationService(UserManager<IdentityUser<Guid>> userManager)
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
            items.Add(new UserSummaryResponse(user.Id, user.Email!, roles.ToArray()));
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
}
