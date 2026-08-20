namespace HarborOps.Modules.Identity.Application;

public sealed class AdminValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class UserNotFoundException(Guid userId)
    : Exception($"User '{userId}' was not found.");

public sealed class RoleNotFoundException : Exception
{
    public RoleNotFoundException(string roleName) : base($"Role '{roleName}' was not found.") { }

    public RoleNotFoundException(Guid roleId) : base($"Role '{roleId}' was not found.") { }
}

public sealed class DuplicateUserEmailException(string email)
    : Exception($"A user with email '{email}' already exists.");

public sealed class DuplicateRoleNameException(string name)
    : Exception($"A role named '{name}' already exists.");

public sealed class FinalIdentityAdministratorException()
    : Exception("This change would remove the final enabled account able to manage Identity users and roles.");

public sealed class SelfAccountLifecycleException()
    : Exception("Administrators cannot disable their own account.");
