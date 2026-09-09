namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record RegisterDefectRequest
{
    public Guid? VesselId { get; init; }

    public Guid? EquipmentId { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? ReportedSeverity { get; init; }

    public DateTime? ObservedAtUtc { get; init; }
}
