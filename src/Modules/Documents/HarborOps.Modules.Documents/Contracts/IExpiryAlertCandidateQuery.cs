namespace HarborOps.Modules.Documents.Contracts;

public interface IExpiryAlertCandidateQuery
{
    Task<IReadOnlyList<ExpiryAlertCandidate>> GetCandidatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public sealed record ExpiryAlertCandidate(Guid SourceId, Guid VesselId, DateOnly ExpiryDate, string SourceType);
