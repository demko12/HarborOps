using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.Modules.Voyages.Application;

internal static class BerthLookupValidator
{
    internal const string PortIdField = "portId";
    internal const string SearchField = "search";
    internal const string TakeField = "take";

    internal const int DefaultTake = 20;
    internal const int MaxTake = 50;
    internal const int MaxSearchLength = Berth.NameMaxLength;

    internal static IReadOnlyDictionary<string, string[]> Validate(BerthLookupRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.PortId is null || request.PortId == Guid.Empty)
        {
            errors[PortIdField] = ["Port is required."];
        }

        if (request.Search is { } search && search.Trim().Length > MaxSearchLength)
        {
            errors[SearchField] = [$"Search must not exceed {MaxSearchLength} characters."];
        }

        if (request.Take is { } take)
        {
            if (take < 1)
            {
                errors[TakeField] = ["Take must be 1 or greater."];
            }
            else if (take > MaxTake)
            {
                errors[TakeField] = [$"Take must not exceed {MaxTake}."];
            }
        }

        return errors;
    }
}
