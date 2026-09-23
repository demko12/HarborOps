namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallStatementSummary(
    Guid StatementId,
    string Status,
    Guid? FinalizedByUserId,
    DateTime? FinalizedAtUtc);
