using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal static class RevokeCrewCertificateValidator
{
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> Validate(RevokeCrewCertificateRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["Reason is required."];
        }
        else if (request.Reason.Trim().Length > CrewCertificate.RevocationReasonMaxLength)
        {
            errors[ReasonField] = [$"Reason must not exceed {CrewCertificate.RevocationReasonMaxLength} characters."];
        }

        return errors;
    }
}
