namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RecordVoyageArrivalResponse(
    Guid Id,
    DateTime ActualArrivalUtc,
    string Status);
