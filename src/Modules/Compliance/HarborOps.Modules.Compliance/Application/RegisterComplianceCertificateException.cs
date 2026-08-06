namespace HarborOps.Modules.Compliance.Application;

internal sealed class RegisterComplianceCertificateValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register compliance certificate validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
