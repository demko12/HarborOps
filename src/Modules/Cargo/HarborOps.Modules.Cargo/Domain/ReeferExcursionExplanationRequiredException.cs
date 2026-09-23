namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferExcursionExplanationRequiredException(Guid excursionId)
    : Exception(
        $"Temperature excursion '{excursionId}' cannot be resolved without an explanation.")
{
    internal Guid ExcursionId { get; } = excursionId;
}
