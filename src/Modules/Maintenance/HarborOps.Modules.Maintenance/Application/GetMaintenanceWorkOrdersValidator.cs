using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class GetMaintenanceWorkOrdersValidator
{
    internal const string VesselIdField = "vesselId";
    internal const string StatusField = "status";
    internal const string PriorityField = "priority";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(
        GetMaintenanceWorkOrdersRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.VesselId == Guid.Empty)
        {
            errors[VesselIdField] = ["VesselId cannot be empty."];
        }

        if (!string.IsNullOrWhiteSpace(request.Status)
            && !Enum.TryParse(request.Status, ignoreCase: true, out WorkOrderStatus _))
        {
            errors[StatusField] = ["Status must be one of: Open, InProgress, Completed, Canceled."];
        }

        if (!string.IsNullOrWhiteSpace(request.Priority)
            && !Enum.TryParse(request.Priority, ignoreCase: true, out WorkOrderPriority _))
        {
            errors[PriorityField] = ["Priority must be one of: Low, Medium, High, Critical."];
        }

        if (request.PageNumber.HasValue && request.PageNumber.Value < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize.HasValue && request.PageSize.Value < 1)
        {
            errors[PageSizeField] = ["Page size must be 1 or greater."];
        }
        else if (request.PageSize.HasValue && request.PageSize.Value > MaxPageSize)
        {
            errors[PageSizeField] = [$"Page size must not exceed {MaxPageSize}."];
        }

        return errors;
    }
}
