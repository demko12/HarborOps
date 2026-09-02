namespace HarborOps.Modules.Compliance.Contracts;

public interface IExpiryAlertCandidateQuery
{
    Task<IReadOnlyList<ComplianceExpiryAlertCandidate>> GetCandidatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public sealed record ComplianceExpiryAlertCandidate(Guid SourceId, Guid VesselId, DateOnly ExpiryDate, string SourceType);
