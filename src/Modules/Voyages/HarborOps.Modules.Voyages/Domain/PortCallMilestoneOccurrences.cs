namespace HarborOps.Modules.Voyages.Domain;

internal sealed record PortCallMilestoneOccurrences(int Count, DateTime? EarliestAtUtc)
{
    internal static readonly PortCallMilestoneOccurrences None = new(0, null);

    internal static PortCallMilestoneOccurrences Once(DateTime occurredAtUtc) => new(1, occurredAtUtc);
}
