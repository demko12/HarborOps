namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RecordVoyageDepartureResponse(
    Guid Id,
    DateTime ActualDepartureUtc,
    string Status);
