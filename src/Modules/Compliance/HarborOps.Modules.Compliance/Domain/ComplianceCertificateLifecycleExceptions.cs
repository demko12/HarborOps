namespace HarborOps.Modules.Compliance.Domain;

internal sealed class InvalidComplianceCertificateTransitionException(
    Guid certificateId,
    string attemptedAction,
    string message)
    : Exception(message)
{
    internal Guid CertificateId { get; } = certificateId;

    internal string AttemptedAction { get; } = attemptedAction;
}
