using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.Modules.Compliance.Application;

internal static class GetNonConformitiesValidator
{
    internal const int MaxPageSize = 100;

    internal const string SeverityField = "severity";
    internal const string ClosureStatusField = "closureStatus";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal static IReadOnlyDictionary<string, string[]> Validate(GetNonConformitiesRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.PageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize < 1 || request.PageSize > MaxPageSize)
        {
            errors[PageSizeField] = [$"Page size must be between 1 and {MaxPageSize}."];
        }

        if (request.Severity is not null
            && request.Severity != NonConformity.SeverityMinor
            && request.Severity != NonConformity.SeverityMajor
            && request.Severity != NonConformity.SeverityCritical)
        {
            errors[SeverityField] =
            [
                $"Severity must be one of '{NonConformity.SeverityMinor}', '{NonConformity.SeverityMajor}', '{NonConformity.SeverityCritical}'.",
            ];
        }

        if (request.ClosureStatus is not null
            && request.ClosureStatus != NonConformity.ClosureStatusOpen
            && request.ClosureStatus != NonConformity.ClosureStatusInProgress
            && request.ClosureStatus != NonConformity.ClosureStatusClosed)
        {
            errors[ClosureStatusField] =
            [
                $"Closure status must be one of '{NonConformity.ClosureStatusOpen}', '{NonConformity.ClosureStatusInProgress}', '{NonConformity.ClosureStatusClosed}'.",
            ];
        }

        return errors;
    }
}
