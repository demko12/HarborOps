namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RecordVoyageArrivalRequest
{
    public DateTime? ActualArrivalUtc { get; init; }
}
