namespace HarborOps.Modules.Compliance.Contracts;

public sealed record GetVesselComplianceOverviewRequest
{
    public Guid VesselId { get; init; }
}
