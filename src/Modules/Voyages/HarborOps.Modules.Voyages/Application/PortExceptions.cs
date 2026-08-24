namespace HarborOps.Modules.Voyages.Application;

internal sealed class SearchPortsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Search ports validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class PortLookupValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Port lookup validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
