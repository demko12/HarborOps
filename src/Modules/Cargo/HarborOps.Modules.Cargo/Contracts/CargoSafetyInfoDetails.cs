namespace HarborOps.Modules.Cargo.Contracts;

public sealed record CargoSafetyInfoDetails(
    Guid CargoSafetyInfoId,
    string ImdgClass,
    string UnNumber,
    string StorageConditions,
    string? HandlingNotes);
