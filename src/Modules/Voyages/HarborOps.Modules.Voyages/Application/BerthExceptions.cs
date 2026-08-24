namespace HarborOps.Modules.Voyages.Application;

internal sealed class RegisterBerthValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register berth validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class DuplicateBerthNameException(Guid portId, string name)
    : Exception($"A berth named '{name}' is already registered for port '{portId}'.")
{
    internal Guid PortId { get; } = portId;

    internal string Name { get; } = name;
}

internal sealed class SearchBerthsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Search berths validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class BerthLookupValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Berth lookup validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
