namespace HarborOps.Modules.Cargo.Contracts;

public sealed record CargoSafetyInfoDetails(
    Guid CargoSafetyInfoId,
    string ImdgClass,
    string UnNumber,
    string StorageConditions,
    string? HandlingNotes,
    string ReviewStatus,
    Guid? SubmittedByUserId,
    DateTime? SubmittedAtUtc,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAtUtc,
    string? ReviewNotes);
