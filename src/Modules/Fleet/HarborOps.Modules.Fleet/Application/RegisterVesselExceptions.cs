namespace HarborOps.Modules.Fleet.Application;

internal sealed class DuplicateImoNumberException(string imoNumber)
    : Exception($"A vessel with IMO number '{imoNumber}' is already registered.")
{
    internal string ImoNumber { get; } = imoNumber;
}

internal sealed class RegisterVesselValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register vessel validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
