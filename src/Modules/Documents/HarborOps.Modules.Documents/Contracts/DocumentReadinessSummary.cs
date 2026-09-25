namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentReadinessSummary(
    int ConfiguredCount,
    int SatisfiedCount,
    int UnsatisfiedCount);
