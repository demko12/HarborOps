namespace HarborOps.Modules.Crew.Application;

internal sealed class DuplicateCrewCertificateLicenseException(Guid crewMemberId, string licenseNumber)
    : Exception($"A certificate with license number '{licenseNumber}' already exists for crew member '{crewMemberId}'.")
{
    internal Guid CrewMemberId { get; } = crewMemberId;
    internal string LicenseNumber { get; } = licenseNumber;
}

internal sealed class RegisterCrewCertificateValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register crew certificate validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
