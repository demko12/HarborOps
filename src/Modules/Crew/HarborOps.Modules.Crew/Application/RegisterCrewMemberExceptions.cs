namespace HarborOps.Modules.Crew.Application;

internal sealed class DuplicateImoSeafarerIdException(string imoSeafarerId)
    : Exception($"A crew member with IMO seafarer ID '{imoSeafarerId}' is already registered.")
{
    internal string ImoSeafarerId { get; } = imoSeafarerId;
}

internal sealed class CrewPositionNotFoundException(Guid baseRankId)
    : Exception($"Crew position with ID '{baseRankId}' was not found.")
{
    internal Guid BaseRankId { get; } = baseRankId;
}

internal sealed class RegisterCrewMemberValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register crew member validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
