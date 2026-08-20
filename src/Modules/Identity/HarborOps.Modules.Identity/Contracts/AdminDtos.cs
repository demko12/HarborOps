namespace HarborOps.Modules.Identity.Contracts;

public sealed class SearchUsersRequest
{
    public string? Search { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}

public sealed record UserSummaryResponse(
    Guid Id,
    string Email,
    IReadOnlyList<string> Roles,
    bool IsActive,
    DateTimeOffset? LockoutEndUtc);

public sealed record SearchUsersResponse(
    IReadOnlyList<UserSummaryResponse> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

public sealed record CreateUserRequest(string Email, string TemporaryPassword);

public sealed record CreateUserResponse(Guid Id, string Email);

public sealed record SetTemporaryPasswordRequest(string TemporaryPassword);

public sealed record RoleResponse(Guid Id, string Name, IReadOnlyList<string> Permissions);

public sealed record CreateRoleRequest(string Name);

public sealed record AssignUserRolesRequest(IReadOnlyList<string> RoleNames);

public sealed record AssignRolePermissionsRequest(IReadOnlyList<string> Permissions);
