using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class RecordCargoTrackingEventValidator
{
    internal const string LocationField = "location";
    internal const string OccurredUtcField = "occurredUtc";
    internal const string StatusField = "status";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        RecordCargoTrackingEventRequest request,
        DateTime currentUtc,
        DateTime bookingCreatedUtc,
        DateTime? latestEventOccurredUtc)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateStatus(request.Status, errors);
        ValidateOccurredUtc(
            request.OccurredUtc,
            currentUtc,
            bookingCreatedUtc,
            latestEventOccurredUtc,
            errors);
        ValidateLocation(request.Location, errors);

        return errors;
    }

    internal static bool TryParseStatus(string? status, out CargoStatus parsedStatus)
    {
        return Enum.TryParse(status, ignoreCase: true, out parsedStatus)
            && Enum.IsDefined(parsedStatus);
    }

    private static void ValidateStatus(string? status, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            errors[StatusField] = ["Status is required."];
            return;
        }

        if (!TryParseStatus(status, out _))
        {
            errors[StatusField] = ["Status is not a known cargo status."];
        }
    }

    private static void ValidateOccurredUtc(
        DateTime? occurredUtc,
        DateTime currentUtc,
        DateTime bookingCreatedUtc,
        DateTime? latestEventOccurredUtc,
        IDictionary<string, string[]> errors)
    {
        if (occurredUtc is null)
        {
            errors[OccurredUtcField] = ["Occurred UTC is required."];
            return;
        }

        if (occurredUtc.Value.Kind != DateTimeKind.Utc)
        {
            errors[OccurredUtcField] = ["Occurred UTC must be in UTC format."];
            return;
        }

        if (occurredUtc.Value > currentUtc)
        {
            errors[OccurredUtcField] = ["Occurred UTC cannot be in the future."];
            return;
        }

        if (occurredUtc.Value < bookingCreatedUtc)
        {
            errors[OccurredUtcField] =
                ["Occurred UTC cannot be before the cargo booking was created."];
            return;
        }

        if (latestEventOccurredUtc is not null && occurredUtc.Value <= latestEventOccurredUtc.Value)
        {
            errors[OccurredUtcField] =
                ["Occurred UTC must be later than the latest tracking event."];
        }
    }

    private static void ValidateLocation(string? location, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            errors[LocationField] = ["Location is required."];
            return;
        }

        if (location.Trim().Length > CargoTrackingEvent.LocationMaxLength)
        {
            errors[LocationField] =
                [$"Location must not exceed {CargoTrackingEvent.LocationMaxLength} characters."];
        }
    }
}
