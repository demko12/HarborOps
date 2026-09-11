namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetRetentionPoliciesResponse(IReadOnlyList<RetentionPolicyListItem> Items);
