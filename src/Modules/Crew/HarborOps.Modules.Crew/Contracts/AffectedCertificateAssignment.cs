namespace HarborOps.Modules.Crew.Contracts;

public sealed record AffectedCertificateAssignment(Guid AssignmentId, Guid VesselId, Guid RankId);
