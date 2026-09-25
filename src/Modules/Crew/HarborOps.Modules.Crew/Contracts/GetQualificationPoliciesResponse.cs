namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetQualificationPoliciesResponse(
    IReadOnlyList<QualificationPolicyListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
