namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferExcursionNotBreachedException(Guid readingId)
    : Exception(
        $"Reading '{readingId}' cannot open a temperature excursion because it is within its thresholds.")
{
    internal Guid ReadingId { get; } = readingId;
}
