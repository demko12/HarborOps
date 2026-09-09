namespace HarborOps.Modules.Voyages.Application;

public sealed class VoyageNotFoundException(Guid voyageId)
    : Exception($"Voyage '{voyageId}' was not found.")
{
    public Guid VoyageId { get; } = voyageId;
}

internal sealed class RecordVoyageDepartureValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Record voyage departure validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
