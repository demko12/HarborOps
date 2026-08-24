namespace HarborOps.Modules.Voyages.Application;

internal sealed class RegisterPortValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register port validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class DuplicateUnLocodeException(string unLocode)
    : Exception($"A port with UN/LOCODE '{unLocode}' is already registered.")
{
    internal string UnLocode { get; } = unLocode;
}

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

internal sealed class PortNotFoundException(Guid portId)
    : Exception($"Port '{portId}' was not found.")
{
    internal Guid PortId { get; } = portId;
}
