using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Fleet.Contracts;

namespace HarborOps.Modules.Fleet.Application;

internal static class RecordVesselPositionValidator
{
    internal const string LatitudeField = "latitude";
    internal const string LongitudeField = "longitude";

    internal const decimal MinLatitude = -90m;
    internal const decimal MaxLatitude = 90m;
    internal const decimal MinLongitude = -180m;
    internal const decimal MaxLongitude = 180m;

    internal static IReadOnlyDictionary<string, string[]> Validate(
        RecordVesselPositionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.Latitude is not { } latitude)
        {
            errors[LatitudeField] = ["Latitude is required."];
        }
        else if (latitude < MinLatitude || latitude > MaxLatitude)
        {
            errors[LatitudeField] =
                [$"Latitude must be between {MinLatitude} and {MaxLatitude}."];
        }

        if (request.Longitude is not { } longitude)
        {
            errors[LongitudeField] = ["Longitude is required."];
        }
        else if (longitude < MinLongitude || longitude > MaxLongitude)
        {
            errors[LongitudeField] =
                [$"Longitude must be between {MinLongitude} and {MaxLongitude}."];
        }

        return errors;
    }
}
