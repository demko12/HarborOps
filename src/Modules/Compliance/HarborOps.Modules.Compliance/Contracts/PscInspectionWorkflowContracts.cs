namespace HarborOps.Modules.Compliance.Contracts;

public sealed record RegisterPscDeficiencyRequest
{
    public string? DeficiencyCode { get; init; }

    public string? Description { get; init; }

    public string? Severity { get; init; }

    public DateOnly? CorrectiveActionDeadline { get; init; }

    public Guid? EvidenceDocumentId { get; init; }
}

public sealed record RegisterPscDeficiencyResponse(int NonConformityId);

public sealed record DetainPscInspectionRequest
{
    public string? AuthorityReference { get; init; }

    public string? Reason { get; init; }
}

public sealed record ReleasePscInspectionRequest
{
    public string? AuthorityReference { get; init; }

    public DateOnly? ReleaseDate { get; init; }

    public Guid? DocumentId { get; init; }
}

public sealed record PscDetentionDecisionItem(
    Guid Id,
    string Decision,
    string AuthorityReference,
    string? Reason,
    DateOnly? ReleaseDate,
    Guid? DocumentId,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc);

public sealed record PscDeficiencyItem(
    int NonConformityId,
    string DeficiencyCode,
    string Description,
    string Severity,
    DateOnly CorrectiveActionDeadline,
    string ClosureStatus,
    Guid EvidenceDocumentId,
    int CorrectiveActionCount);

public sealed record GetPscInspectionDetailResponse(
    int Id,
    Guid VesselId,
    Guid PortId,
    DateOnly InspectionDate,
    string InspectingAuthority,
    int ReportedDeficiencyCount,
    int RegisteredDeficiencyCount,
    int OpenDeficiencyCount,
    int ClosedDeficiencyCount,
    bool HasDeficiencyCountMismatch,
    string DetentionState,
    PscDetentionDecisionItem? LatestDecision,
    IReadOnlyList<PscDetentionDecisionItem> DetentionHistory);

public sealed record GetPscDeficienciesResponse(IReadOnlyList<PscDeficiencyItem> Items);

public interface IGetPscInspectionDetailQuery
{
    Task<GetPscInspectionDetailResponse?> GetAsync(int pscInspectionId, CancellationToken cancellationToken);
}

public interface IGetPscDeficienciesQuery
{
    Task<GetPscDeficienciesResponse?> GetAsync(int pscInspectionId, CancellationToken cancellationToken);
}

public interface IGetPscDetentionHistoryQuery
{
    Task<IReadOnlyList<PscDetentionDecisionItem>?> GetAsync(int pscInspectionId, CancellationToken cancellationToken);
}
