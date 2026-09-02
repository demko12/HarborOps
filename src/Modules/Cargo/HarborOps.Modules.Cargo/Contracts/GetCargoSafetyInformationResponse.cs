namespace HarborOps.Modules.Cargo.Contracts;

public sealed record GetCargoSafetyInformationResponse(
    CargoSafetyInfoDetails Declaration,
    IReadOnlyList<CargoSafetyReviewEntryListItem> Timeline);
