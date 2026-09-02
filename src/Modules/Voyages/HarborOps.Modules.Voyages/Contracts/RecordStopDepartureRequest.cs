namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RecordStopDepartureRequest
{
    public DateTime? ActualDepartureUtc { get; init; }
}
