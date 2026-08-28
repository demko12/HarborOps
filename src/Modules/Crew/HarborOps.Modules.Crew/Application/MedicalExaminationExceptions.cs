namespace HarborOps.Modules.Crew.Application;

internal sealed class GetMedicalExaminationsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get medical examinations validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class RegisterMedicalExaminationValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register medical examination validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
