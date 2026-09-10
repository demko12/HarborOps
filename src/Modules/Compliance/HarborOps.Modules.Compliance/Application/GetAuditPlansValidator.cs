using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.Modules.Compliance.Application;

internal static class GetAuditPlansValidator
{
    internal const int MaxPageSize = 100;

    internal const string TypeField = "type";
    internal const string StatusField = "status";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";
    internal const string DateRangeField = "dateRange";

    private static readonly string[] SupportedStatuses =
    [
        AuditPlan.ScheduledStatus,
        AuditPlan.InProgressStatus,
        AuditPlan.CompletedStatus,
        AuditPlan.CancelledStatus,
    ];

    internal static IReadOnlyDictionary<string, string[]> Validate(GetAuditPlansRequest request)
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

        if (!string.IsNullOrWhiteSpace(request.Type) && !SurveyAudit.SupportedTypes.Contains(request.Type.Trim()))
        {
            errors[TypeField] = ["Select a supported audit type."];
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && !SupportedStatuses.Contains(request.Status.Trim()))
        {
            errors[StatusField] = ["Select a supported status."];
        }

        if (request.From is { } from && request.To is { } to && from > to)
        {
            errors[DateRangeField] = ["From date must not be after to date."];
        }

        return errors;
    }
}
