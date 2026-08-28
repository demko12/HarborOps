using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Modules.Voyages.Application;

internal static class PlanVoyageItineraryValidator
{
    internal const string StopsField = "stops";

    internal const int MinimumStops = 2;

    internal static IReadOnlyDictionary<string, string[]> ValidateShape(
        PlanVoyageItineraryRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var stops = request.Stops;

        if (stops is null || stops.Count < MinimumStops)
        {
            errors[StopsField] = [$"An itinerary must have at least {MinimumStops} stops."];
            return errors;
        }

        var stopErrors = new List<string>();

        if (stops.Any(stop => stop.StopOrder is null || stop.StopOrder < 1))
        {
            stopErrors.Add("Every stop must have a stop order of 1 or greater.");
        }

        if (stops.Any(stop => stop.Eta is null || stop.Eta.Value.Kind != DateTimeKind.Utc))
        {
            stopErrors.Add("Every stop must have a UTC estimated time of arrival.");
        }

        if (stops.Any(stop => stop.Etd is null || stop.Etd.Value.Kind != DateTimeKind.Utc))
        {
            stopErrors.Add("Every stop must have a UTC estimated time of departure.");
        }

        if (stopErrors.Count > 0)
        {
            errors[StopsField] = [.. stopErrors];
            return errors;
        }

        var orderedStops = OrderStops(stops);

        if (!IsContiguousFromOne(orderedStops))
        {
            errors[StopsField] =
                ["Stop orders must be unique and numbered consecutively starting at 1."];
            return errors;
        }

        ValidateChronology(orderedStops, stopErrors);

        if (stopErrors.Count > 0)
        {
            errors[StopsField] = [.. stopErrors];
        }

        return errors;
    }

    internal static void ValidateStopSourcing(
        IReadOnlyList<PlanVoyageItineraryStop> orderedStops,
        int? routeStopCount,
        IDictionary<string, string[]> errors)
    {
        if (routeStopCount is { } count && orderedStops.Count < count)
        {
            errors[StopsField] =
                [$"The itinerary must include all {count} stops from the selected route."];
            return;
        }

        var stopErrors = new List<string>();

        for (var index = 0; index < orderedStops.Count; index++)
        {
            var stop = orderedStops[index];
            var isRouteCovered = routeStopCount is { } routeCount
                && stop.StopOrder!.Value <= routeCount;

            if (isRouteCovered)
            {
                if (stop.PortId is not null
                    || stop.DistanceFromPrevious is not null
                    || stop.EstimatedDurationFromPrevious is not null)
                {
                    stopErrors.Add(
                        $"Stop {stop.StopOrder} is covered by the selected route and must not "
                        + "specify a port, distance, or duration.");
                }

                continue;
            }

            if (stop.PortId is null || stop.PortId == Guid.Empty)
            {
                stopErrors.Add($"Stop {stop.StopOrder} must reference a port.");
            }

            if (index == 0)
            {
                if (stop.DistanceFromPrevious != 0 || stop.EstimatedDurationFromPrevious != 0)
                {
                    stopErrors.Add(
                        "The first stop must have no distance or duration from a previous port.");
                }

                continue;
            }

            if (stop.DistanceFromPrevious is null || stop.DistanceFromPrevious <= 0)
            {
                stopErrors.Add(
                    $"Stop {stop.StopOrder} must have a positive distance from the previous port.");
            }

            if (stop.EstimatedDurationFromPrevious is null || stop.EstimatedDurationFromPrevious <= 0)
            {
                stopErrors.Add(
                    $"Stop {stop.StopOrder} must have a positive duration from the previous port.");
            }
        }

        if (stopErrors.Count > 0)
        {
            errors[StopsField] = [.. stopErrors];
        }
    }

    internal static IReadOnlyList<PlanVoyageItineraryStop> OrderStops(
        IReadOnlyList<PlanVoyageItineraryStop> stops)
    {
        return [.. stops.OrderBy(stop => stop.StopOrder!.Value)];
    }

    private static bool IsContiguousFromOne(IReadOnlyList<PlanVoyageItineraryStop> orderedStops)
    {
        for (var index = 0; index < orderedStops.Count; index++)
        {
            if (orderedStops[index].StopOrder!.Value != index + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateChronology(
        IReadOnlyList<PlanVoyageItineraryStop> orderedStops,
        List<string> stopErrors)
    {
        DateTime? previousEtd = null;

        foreach (var stop in orderedStops)
        {
            var eta = stop.Eta!.Value;
            var etd = stop.Etd!.Value;

            if (etd <= eta)
            {
                stopErrors.Add(
                    "Each stop's estimated departure must be later than its estimated arrival.");
                return;
            }

            if (previousEtd is { } previous && eta <= previous)
            {
                stopErrors.Add(
                    "Estimated arrival and departure times must increase across stops in order.");
                return;
            }

            previousEtd = etd;
        }
    }
}
