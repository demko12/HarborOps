using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class GetDefectsValidator
{
    internal const string StatusField = "status";
    internal const string SeverityField = "severity";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(GetDefectsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(request.Status)
            && !Enum.TryParse(request.Status, ignoreCase: true, out DefectStatus _))
        {
            errors[StatusField] = ["Status must be one of: Reported, Triaged, Converted, Rejected."];
        }

        if (!string.IsNullOrWhiteSpace(request.Severity)
            && !Enum.TryParse(request.Severity, ignoreCase: true, out DefectSeverity _))
        {
            errors[SeverityField] = ["Severity must be one of: Low, Medium, High, Critical."];
        }

        if (request.PageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize < 1)
        {
            errors[PageSizeField] = ["Page size must be 1 or greater."];
        }
        else if (request.PageSize > MaxPageSize)
        {
            errors[PageSizeField] = [$"Page size must not exceed {MaxPageSize}."];
        }

        return errors;
    }
}
