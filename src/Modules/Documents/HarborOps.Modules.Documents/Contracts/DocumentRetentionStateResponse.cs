namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentRetentionStateResponse(Guid DocumentId, string State);
