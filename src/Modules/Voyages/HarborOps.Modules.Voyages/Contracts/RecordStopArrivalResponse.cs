namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RecordStopArrivalResponse(
    Guid StopId,
    DateTime ActualArrivalUtc,
    string Status);
