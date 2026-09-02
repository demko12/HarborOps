namespace HarborOps.Modules.Crew.Contracts;

public interface IExpiryAlertCandidateQuery
{
    Task<IReadOnlyList<CrewExpiryAlertCandidate>> GetCandidatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public sealed record CrewExpiryAlertCandidate(Guid SourceId, Guid CrewMemberId, DateOnly ExpiryDate, string SourceType);
