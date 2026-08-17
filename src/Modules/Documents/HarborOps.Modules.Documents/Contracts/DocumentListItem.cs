namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentListItem(
    Guid DocumentId,
    Guid VesselId,
    string Number,
    string DocumentTypeName,
    string Status,
    DateOnly ExpiryDate);
