namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewCertificateHistoryResponse(
    Guid CertificateId,
    IReadOnlyList<Guid> ChainCertificateIds,
    IReadOnlyList<CrewCertificateHistoryItem> History);
