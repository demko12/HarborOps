namespace HarborOps.Modules.Maintenance.Application;

internal sealed class RegisterEquipmentValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register equipment validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetEquipmentValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get equipment validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
