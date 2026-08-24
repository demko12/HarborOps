using System.Globalization;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.Modules.Voyages.Application;

internal static class RegisterRouteValidator
{
    internal const string DescriptionField = "description";
    internal const string EstimatedDistanceField = "estimatedDistance";
    internal const string EstimatedDurationField = "estimatedDuration";
    internal const string NameField = "name";
    internal const string StopsField = "stops";

    internal const int MinimumStops = 2;

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterRouteRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateName(request.Name, errors);
        ValidateDescription(request.Description, errors);
        ValidateEstimatedDistance(request.EstimatedDistance, errors);
        ValidateEstimatedDuration(request.EstimatedDuration, errors);
        ValidateStops(request, errors);

        return errors;
    }

    internal static IReadOnlyList<RegisterRouteStop> OrderStops(
        IReadOnlyList<RegisterRouteStop> stops)
    {
        return [.. stops.OrderBy(stop => stop.StopOrder!.Value)];
    }

    private static void ValidateStops(
        RegisterRouteRequest request,
        IDictionary<string, string[]> errors)
    {
        var stops = request.Stops;

        if (stops is null || stops.Count < MinimumStops)
        {
            errors[StopsField] = [$"A route must have at least {MinimumStops} port stops."];
            return;
        }

        var stopErrors = new List<string>();

        if (stops.Any(stop => stop.PortId is null || stop.PortId == Guid.Empty))
        {
            stopErrors.Add("Every route stop must reference a port.");
        }

        if (stops.Any(stop => stop.StopOrder is null || stop.StopOrder < 1))
        {
            stopErrors.Add("Every route stop must have a stop order of 1 or greater.");
        }

        if (stops.Any(stop => stop.DistanceFromPrevious is null || stop.DistanceFromPrevious < 0))
        {
            stopErrors.Add("Route stop distances must not be negative.");
        }

        if (stops.Any(stop => stop.EstimatedDurationFromPrevious is null
            || stop.EstimatedDurationFromPrevious < 0))
        {
            stopErrors.Add("Route stop durations must not be negative.");
        }

        if (stopErrors.Count > 0)
        {
            errors[StopsField] = [.. stopErrors];
            return;
        }

        if (stops.Select(stop => stop.StopOrder!.Value).Distinct().Count() != stops.Count)
        {
            stopErrors.Add("Route stop orders must be unique.");
        }

        if (stops.Select(stop => stop.PortId!.Value).Distinct().Count() != stops.Count)
        {
            stopErrors.Add("A port must not appear more than once in a route.");
        }

        if (stopErrors.Count > 0)
        {
            errors[StopsField] = [.. stopErrors];
            return;
        }

        ValidateLegs(stops, stopErrors);
        ValidateTotals(request, stops, errors);

        if (stopErrors.Count > 0)
        {
            errors[StopsField] = [.. stopErrors];
        }
    }

    private static void ValidateLegs(
        IReadOnlyList<RegisterRouteStop> stops,
        List<string> stopErrors)
    {
        var orderedStops = OrderStops(stops);
        var firstStop = orderedStops[0];

        if (firstStop.DistanceFromPrevious != 0 || firstStop.EstimatedDurationFromPrevious != 0)
        {
            stopErrors.Add(
                "The first route stop must have no distance or duration from a previous port.");
        }

        if (orderedStops.Skip(1).Any(stop => stop.DistanceFromPrevious <= 0
            || stop.EstimatedDurationFromPrevious <= 0))
        {
            stopErrors.Add(
                "Every route stop after the first must have a positive distance and duration from "
                + "the previous port.");
        }
    }

    private static void ValidateTotals(
        RegisterRouteRequest request,
        IReadOnlyList<RegisterRouteStop> stops,
        IDictionary<string, string[]> errors)
    {
        if (!errors.ContainsKey(EstimatedDistanceField))
        {
            var totalDistance = stops.Sum(stop => stop.DistanceFromPrevious!.Value);

            if (request.EstimatedDistance!.Value != totalDistance)
            {
                errors[EstimatedDistanceField] =
                [
                    "Estimated distance must equal the sum of the stop distances "
                    + $"({totalDistance.ToString(CultureInfo.InvariantCulture)}).",
                ];
            }
        }

        if (!errors.ContainsKey(EstimatedDurationField))
        {
            var totalDuration = stops.Sum(stop => stop.EstimatedDurationFromPrevious!.Value);

            if (request.EstimatedDuration!.Value != totalDuration)
            {
                errors[EstimatedDurationField] =
                [
                    "Estimated duration must equal the sum of the stop durations "
                    + $"({totalDuration.ToString(CultureInfo.InvariantCulture)}).",
                ];
            }
        }
    }

    private static void ValidateName(string? value, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[NameField] = ["Route name is required."];
            return;
        }

        if (value.Trim().Length > Route.NameMaxLength)
        {
            errors[NameField] = [$"Route name must not exceed {Route.NameMaxLength} characters."];
        }
    }

    private static void ValidateDescription(string? value, IDictionary<string, string[]> errors)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > Route.DescriptionMaxLength)
        {
            errors[DescriptionField] =
                [$"Description must not exceed {Route.DescriptionMaxLength} characters."];
        }
    }

    private static void ValidateEstimatedDistance(
        decimal? value,
        IDictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[EstimatedDistanceField] = ["Estimated distance is required."];
            return;
        }

        if (value.Value <= 0)
        {
            errors[EstimatedDistanceField] = ["Estimated distance must be greater than zero."];
        }
    }

    private static void ValidateEstimatedDuration(
        int? value,
        IDictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[EstimatedDurationField] = ["Estimated duration is required."];
            return;
        }

        if (value.Value <= 0)
        {
            errors[EstimatedDurationField] = ["Estimated duration must be greater than zero."];
        }
    }
}
