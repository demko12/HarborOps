namespace HarborOps.Web.Api;

internal sealed record AdminUserSummary(Guid Id, string Email, IReadOnlyList<string> Roles);

internal sealed record AdminUserPage(
    IReadOnlyList<AdminUserSummary> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record AdminRole(Guid Id, string Name, IReadOnlyList<string> Permissions);

internal sealed record CreateAdminUserRequest(string Email, string TemporaryPassword);

internal sealed record CreateAdminRoleRequest(string Name);

internal sealed record ReplaceUserRolesRequest(IReadOnlyList<string> RoleNames);

internal sealed record ReplaceRolePermissionsRequest(IReadOnlyList<string> Permissions);

internal sealed record AdminApiResult<T>(T? Value, AdminApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record AdminApiFailure(int StatusCode, string Title, string Message)
{
    internal bool IsForbidden => StatusCode == StatusCodes.Status403Forbidden;

    internal bool IsConflict => StatusCode == StatusCodes.Status409Conflict;
}
