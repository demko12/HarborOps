namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewAssignmentQualificationEvidence
{
    internal const int CertificateIdsSnapshotMaxLength = 4000;

    private CrewAssignmentQualificationEvidence()
    {
    }

    private CrewAssignmentQualificationEvidence(
        Guid id,
        Guid assignmentId,
        Guid policyId,
        Guid policyConcurrencyTokenAtDecision,
        string certificateIdsSnapshot,
        Guid recordedByUserId,
        DateTime recordedAtUtc)
    {
        Id = id;
        AssignmentId = assignmentId;
        PolicyId = policyId;
        PolicyConcurrencyTokenAtDecision = policyConcurrencyTokenAtDecision;
        CertificateIdsSnapshot = certificateIdsSnapshot;
        RecordedByUserId = recordedByUserId;
        RecordedAtUtc = recordedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }
    public Guid PolicyId { get; private set; }
    public Guid PolicyConcurrencyTokenAtDecision { get; private set; }
    public string CertificateIdsSnapshot { get; private set; } = null!;
    public Guid RecordedByUserId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }

    internal static CrewAssignmentQualificationEvidence Record(
        Guid assignmentId,
        Guid policyId,
        Guid policyConcurrencyTokenAtDecision,
        string certificateIdsSnapshotJson,
        Guid recordedByUserId,
        DateTime recordedAtUtc)
    {
        return new CrewAssignmentQualificationEvidence(
            Guid.NewGuid(),
            assignmentId,
            policyId,
            policyConcurrencyTokenAtDecision,
            certificateIdsSnapshotJson,
            recordedByUserId,
            recordedAtUtc);
    }
}
