namespace HarborOps.Modules.Crew.Contracts;

public interface IOperationsDashboardCrewQuery
{
    Task<OperationsDashboardCrewResult> GetExpiringCredentialsAsync(
        DateOnly asOf,
        DateOnly through,
        int maximumItems,
        CancellationToken cancellationToken);
}

public sealed record OperationsDashboardCrewResult(
    int ExpiringCredentialCount,
    IReadOnlyList<OperationsDashboardCrewItem> Items);

public sealed record OperationsDashboardCrewItem(
    Guid CrewMemberId,
    string CrewMemberName,
    string CredentialName,
    DateOnly ExpiryDate,
    bool IsPassport);
