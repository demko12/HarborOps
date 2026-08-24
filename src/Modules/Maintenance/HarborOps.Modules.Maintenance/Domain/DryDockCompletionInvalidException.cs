namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class DryDockCompletionInvalidException(Guid dryDockId, string reason)
    : Exception($"Dry dock '{dryDockId}' cannot be completed: {reason}")
{
    internal Guid DryDockId { get; } = dryDockId;

    internal string Reason { get; } = reason;
}
