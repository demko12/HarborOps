namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RecordStopDepartureResponse(
    Guid StopId,
    DateTime ActualDepartureUtc,
    string Status);
