namespace HarborOps.Modules.Documents.Application;

internal sealed class LegalHoldNotFoundException(Guid holdId)
    : Exception($"Legal hold '{holdId}' was not found.")
{
    internal Guid HoldId { get; } = holdId;
}
