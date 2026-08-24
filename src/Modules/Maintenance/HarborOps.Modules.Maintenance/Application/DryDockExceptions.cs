namespace HarborOps.Modules.Maintenance.Application;

internal sealed class ScheduleDryDockValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Schedule dry dock validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetDryDocksValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get dry docks validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class DryDockNotFoundException(Guid dryDockId)
    : Exception($"Dry dock '{dryDockId}' was not found.")
{
    internal Guid DryDockId { get; } = dryDockId;
}

internal sealed class CompleteDryDockValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Complete dry dock validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
