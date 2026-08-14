namespace HarborOps.Modules.Identity.Contracts;

public sealed record AccountResponse(Guid Id, string Email, IReadOnlyList<string> Permissions);
