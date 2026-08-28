namespace HarborOps.Modules.Identity.Contracts;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
