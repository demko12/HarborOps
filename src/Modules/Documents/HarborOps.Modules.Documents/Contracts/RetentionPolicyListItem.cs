namespace HarborOps.Modules.Documents.Contracts;

public sealed record RetentionPolicyListItem(
    int DocumentTypeId,
    string DocumentTypeName,
    int RetentionPeriodDays);
