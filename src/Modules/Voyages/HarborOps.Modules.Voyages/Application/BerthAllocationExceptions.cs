namespace HarborOps.Modules.Voyages.Application;

internal sealed class BerthAvailabilityValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Berth availability validation failed.")
{ internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors; }

internal sealed class ReplaceBerthAllocationValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Berth allocation validation failed.")
{ internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors; }

internal sealed class BerthDimensionMismatchException(Guid berthId)
    : Exception($"Berth '{berthId}' cannot accommodate the requested vessel dimensions.");

internal sealed class VoyageBerthAllocationNotAllowedException(Guid voyageId, string status)
    : Exception($"Voyage '{voyageId}' cannot be rescheduled from status '{status}'.");
