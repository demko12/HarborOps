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

internal sealed class AssignBerthValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Assign berth validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class BerthNotFoundException(Guid berthId)
    : Exception($"Berth '{berthId}' was not found.")
{
    internal Guid BerthId { get; } = berthId;
}

internal sealed class BerthPortMismatchException(Guid berthId, Guid portId)
    : Exception($"Berth '{berthId}' does not belong to port '{portId}'.")
{
    internal Guid BerthId { get; } = berthId;

    internal Guid PortId { get; } = portId;
}

internal sealed class BerthOverlapException(Guid berthId, DateTime startTime, DateTime endTime, Exception? innerException = null)
    : Exception(
        $"Berth '{berthId}' already has an active schedule overlapping "
        + $"{startTime:O}–{endTime:O}.", innerException)
{
    internal Guid BerthId { get; } = berthId;

    internal DateTime StartTime { get; } = startTime;

    internal DateTime EndTime { get; } = endTime;
}
