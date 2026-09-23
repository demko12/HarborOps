namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetVesselManningPlansRequest
{
    public Guid? VesselId { get; init; }
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }
}
