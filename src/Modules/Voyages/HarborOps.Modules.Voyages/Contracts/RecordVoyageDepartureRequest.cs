namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RecordVoyageDepartureRequest
{
    public DateTime? ActualDepartureUtc { get; init; }
}
