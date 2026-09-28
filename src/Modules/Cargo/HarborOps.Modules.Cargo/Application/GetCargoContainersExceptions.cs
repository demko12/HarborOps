namespace HarborOps.Modules.Cargo.Application;

internal sealed class GetCargoContainersValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get cargo containers validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
