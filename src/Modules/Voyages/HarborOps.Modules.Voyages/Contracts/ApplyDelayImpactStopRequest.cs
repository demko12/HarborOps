namespace HarborOps.Modules.Voyages.Contracts;

public sealed record ApplyDelayImpactStopRequest
{
    public Guid? VoyageStopId { get; init; }

    public DateTime? ExpectedEta { get; init; }

    public DateTime? ExpectedEtd { get; init; }

    public DateTime? Eta { get; init; }

    public DateTime? Etd { get; init; }
}
