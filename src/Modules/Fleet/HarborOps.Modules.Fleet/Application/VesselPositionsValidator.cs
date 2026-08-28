using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;

namespace HarborOps.Modules.Fleet.Application;

internal static class VesselPositionsValidator
{
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";
    internal const string FleetIdField = "fleetId";
    internal const string StatusField = "status";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 50;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(VesselPositionsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.PageNumber is { } pageNumber && pageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize is { } pageSize)
        {
            if (pageSize < 1)
            {
                errors[PageSizeField] = ["Page size must be 1 or greater."];
            }
            else if (pageSize > MaxPageSize)
            {
                errors[PageSizeField] = [$"Page size must not exceed {MaxPageSize}."];
            }
        }

        if (request.FleetId == Guid.Empty)
        {
            errors[FleetIdField] = ["Fleet is required when supplied."];
        }

        if (request.Status is { Length: > 0 } status && !VesselStatus.IsValid(status.Trim()))
        {
            errors[StatusField] =
                [$"Status must be one of: {string.Join(", ", VesselStatus.All)}."];
        }

        return errors;
    }
}
