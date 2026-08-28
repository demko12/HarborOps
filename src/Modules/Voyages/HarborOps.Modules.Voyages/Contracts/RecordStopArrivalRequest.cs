namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RecordStopArrivalRequest
{
    public DateTime? ActualArrivalUtc { get; init; }
}
