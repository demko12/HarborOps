using HarborOps.Web.Api;

namespace HarborOps.Web.Components.Pages.Administration;

public partial class Administration
{
    private const int pageSize = 10;

    private static readonly IReadOnlyList<PermissionOption> PermissionCatalog = [
        new("fleet.read", "Fleet", "Read"),
        new("fleet.manage", "Fleet", "Manage"),
        new("crew.read", "Crew", "Read"),
        new("crew.manage", "Crew", "Manage"),
        new("voyages.read", "Voyages", "Read"),
        new("voyages.manage", "Voyages", "Manage"),
        new("cargo.read", "Cargo", "Read"),
        new("cargo.manage", "Cargo", "Manage"),
        new("maintenance.read", "Maintenance", "Read"),
        new("maintenance.manage", "Maintenance", "Manage"),
        new("compliance.read", "Compliance", "Read"),
        new("compliance.manage", "Compliance", "Manage"),
        new("documents.read", "Documents", "Read"),
        new("documents.manage", "Documents", "Manage"),
        new("reports.read", "Reports", "Read"),
        new("reports.manage", "Reports", "Manage"),
        new("identity.users.read", "Identity users", "Read"),
        new("identity.users.manage", "Identity users", "Manage"),
        new("identity.roles.read", "Identity roles", "Read"),
        new("identity.roles.manage", "Identity roles", "Manage"),
    ];

    private readonly HashSet<string> selectedRoleNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selectedPermissions = new(StringComparer.OrdinalIgnoreCase);
    private readonly CreateUserModel newUser = new();

    private List<AdminUserSummary> users = [];
    private List<AdminRole> roles = [];
    private AdminUserSummary? selectedUser;
    private AdminRole? selectedRole;
    private AdminApiFailure? usersFailure;
    private AdminApiFailure? rolesFailure;

    private string? search;
    private string? newRoleName;
    private string? userFormMessage;
    private string? roleFormMessage;
    private string? editorMessage;
    private string userFormMessageClass = string.Empty;
    private string roleFormMessageClass = string.Empty;
    private string editorMessageClass = string.Empty;

    private int pageNumber = 1;
    private int totalUsers;

    private bool isLoadingUsers = true;
    private bool isLoadingRoles = true;
    private bool isCreatingUser;
    private bool isCreatingRole;
    private bool isSaving;
    private bool confirmUserRoles;
    private bool confirmRolePermissions;
    private bool forbidden;

    protected override async Task OnInitializedAsync()
    {
        if (AuthorizationState.CanManageAdministration)
        {
            await Task.WhenAll(LoadUsersAsync(), LoadRolesAsync());
        }
    }

    private async Task SearchAsync()
    {
        pageNumber = 1;
        await LoadUsersAsync();
    }

    private async Task PreviousPageAsync()
    {
        pageNumber--;
        await LoadUsersAsync();
    }

    private async Task NextPageAsync()
    {
        pageNumber++;
        await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        isLoadingUsers = true;
        usersFailure = null;

        var result = await AdministrationApi.SearchUsersAsync(
            search,
            pageNumber,
            pageSize,
            CancellationToken.None);

        isLoadingUsers = false;

        if (!result.IsSuccess)
        {
            usersFailure = result.Failure;
            forbidden |= result.Failure!.IsForbidden;
            return;
        }

        var value = result.Value!;
        users = value.Items.ToList();
        pageNumber = value.PageNumber;
        totalUsers = value.TotalCount;
    }

    private async Task LoadRolesAsync()
    {
        isLoadingRoles = true;
        rolesFailure = null;

        var result = await AdministrationApi.ListRolesAsync(CancellationToken.None);
        isLoadingRoles = false;

        if (!result.IsSuccess)
        {
            rolesFailure = result.Failure;
            forbidden |= result.Failure!.IsForbidden;
            return;
        }

        roles = result.Value!.ToList();
    }

    private async Task CreateUserAsync()
    {
        userFormMessage = null;

        if (newUser.TemporaryPassword != newUser.Confirmation)
        {
            userFormMessage = "Temporary passwords do not match.";
            userFormMessageClass = "error";
            return;
        }

        isCreatingUser = true;

        try
        {
            var result = await AdministrationApi.CreateUserAsync(
                new(newUser.Email, newUser.TemporaryPassword),
                CancellationToken.None);

            if (!result.IsSuccess)
            {
                SetUserMessage(result.Failure!);
                return;
            }

            newUser.Email = string.Empty;
            userFormMessage = "User created successfully.";
            userFormMessageClass = "success";
            await LoadUsersAsync();
        }
        finally
        {
            newUser.TemporaryPassword = string.Empty;
            newUser.Confirmation = string.Empty;
            isCreatingUser = false;
        }
    }

    private async Task CreateRoleAsync()
    {
        roleFormMessage = null;
        isCreatingRole = true;

        try
        {
            var result = await AdministrationApi.CreateRoleAsync(
                new(newRoleName ?? string.Empty),
                CancellationToken.None);

            if (!result.IsSuccess)
            {
                roleFormMessage = result.Failure!.Message;
                roleFormMessageClass = "error";
                forbidden |= result.Failure.IsForbidden;
                return;
            }

            newRoleName = string.Empty;
            roleFormMessage = "Role created successfully.";
            roleFormMessageClass = "success";
            await LoadRolesAsync();
        }
        finally
        {
            isCreatingRole = false;
        }
    }

    private void SelectUser(AdminUserSummary user)
    {
        selectedRole = null;
        selectedUser = user;
        selectedRoleNames.Clear();
        selectedRoleNames.UnionWith(user.Roles);
        ResetEditor();
    }

    private void SelectRole(AdminRole role)
    {
        selectedUser = null;
        selectedRole = role;
        selectedPermissions.Clear();
        selectedPermissions.UnionWith(role.Permissions);
        ResetEditor();
    }

    private void ToggleRole(string name, object? isSelected)
    {
        if (isSelected is true)
        {
            selectedRoleNames.Add(name);
        }
        else
        {
            selectedRoleNames.Remove(name);
        }
    }

    private void TogglePermission(string name, object? isSelected)
    {
        if (isSelected is true)
        {
            selectedPermissions.Add(name);
        }
        else
        {
            selectedPermissions.Remove(name);
        }
    }

    private async Task SaveUserRolesAsync()
    {
        if (selectedUser is null)
        {
            return;
        }

        isSaving = true;
        var result = await AdministrationApi.ReplaceUserRolesAsync(
            selectedUser.Id,
            new(selectedRoleNames.ToList()),
            CancellationToken.None);
        isSaving = false;

        if (!result.IsSuccess)
        {
            SetEditorMessage(result.Failure!);
            return;
        }

        confirmUserRoles = false;
        editorMessage = "Roles replaced successfully.";
        editorMessageClass = "success";

        if (AuthorizationState.UserId == selectedUser.Id)
        {
            AuthorizationState.RefreshVisibleAuthorization();
        }

        await LoadUsersAsync();
    }

    private async Task SaveRolePermissionsAsync()
    {
        if (selectedRole is null)
        {
            return;
        }

        isSaving = true;
        var result = await AdministrationApi.ReplaceRolePermissionsAsync(
            selectedRole.Id,
            new(selectedPermissions.ToList()),
            CancellationToken.None);
        isSaving = false;

        if (!result.IsSuccess)
        {
            SetEditorMessage(result.Failure!);
            return;
        }

        confirmRolePermissions = false;
        editorMessage = "Permissions replaced successfully.";
        editorMessageClass = "success";
        AuthorizationState.RefreshVisibleAuthorization();
        await LoadRolesAsync();
    }

    private void SetUserMessage(AdminApiFailure failure)
    {
        userFormMessage = failure.Message;
        userFormMessageClass = "error";
        forbidden |= failure.IsForbidden;
    }

    private void SetEditorMessage(AdminApiFailure failure)
    {
        editorMessage = failure.IsConflict
            ? "Final administrator protection prevented this change. Your selections are preserved."
            : failure.Message;
        editorMessageClass = "error";
        forbidden |= failure.IsForbidden;
    }

    private void ResetEditor()
    {
        editorMessage = null;
        confirmUserRoles = false;
        confirmRolePermissions = false;
    }

    public void Dispose()
    {
        newUser.TemporaryPassword = string.Empty;
        newUser.Confirmation = string.Empty;
    }

    private sealed record PermissionOption(string Name, string Capability, string Action);

    private sealed class CreateUserModel
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.EmailAddress]
        public string Email { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        public string TemporaryPassword { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        public string Confirmation { get; set; } = string.Empty;
    }
}
