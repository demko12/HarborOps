using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Modules.Voyages.Application;

internal static class AmendVoyageScheduleValidator
{
    internal const string ArrivalPortIdField = "arrivalPortId";
    internal const string DeparturePortIdField = "departurePortId";
    internal const string PlannedArrivalUtcField = "plannedArrivalUtc";
    internal const string PlannedDepartureUtcField = "plannedDepartureUtc";
    internal const string ReasonField = "reason";
    internal const string RouteIdField = "routeId";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        AmendVoyageScheduleRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredPort(request.DeparturePortId, DeparturePortIdField, "Departure port", errors);
        ValidateRequiredPort(request.ArrivalPortId, ArrivalPortIdField, "Arrival port", errors);
        ValidateDifferentPorts(request, errors);

        if (request.RouteId == Guid.Empty)
        {
            errors[RouteIdField] = ["Route must be a valid identifier when supplied."];
        }

        ValidateUtcTimestamp(
            request.PlannedDepartureUtc,
            PlannedDepartureUtcField,
            "Planned departure",
            errors);

        ValidateUtcTimestamp(
            request.PlannedArrivalUtc,
            PlannedArrivalUtcField,
            "Planned arrival",
            errors);

        ValidateArrivalAfterDeparture(request, errors);
        VoyageScheduleReasonValidator.Validate(request.Reason, ReasonField, errors);

        return errors;
    }

    private static void ValidateRequiredPort(
        Guid? value,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (value is null || value == Guid.Empty)
        {
            errors[field] = [$"{displayName} is required."];
        }
    }

    private static void ValidateDifferentPorts(
        AmendVoyageScheduleRequest request,
        IDictionary<string, string[]> errors)
    {
        if (errors.ContainsKey(DeparturePortIdField) || errors.ContainsKey(ArrivalPortIdField))
        {
            return;
        }

        if (request.DeparturePortId == request.ArrivalPortId)
        {
            errors[ArrivalPortIdField] = ["Departure and arrival ports must be different."];
        }
    }

    private static void ValidateUtcTimestamp(
        DateTime? value,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[field] = [$"{displayName} is required."];
            return;
        }

        if (value.Value.Kind != DateTimeKind.Utc)
        {
            errors[field] = [$"{displayName} must be a UTC timestamp."];
        }
    }

    private static void ValidateArrivalAfterDeparture(
        AmendVoyageScheduleRequest request,
        IDictionary<string, string[]> errors)
    {
        if (errors.ContainsKey(PlannedDepartureUtcField)
            || errors.ContainsKey(PlannedArrivalUtcField))
        {
            return;
        }

        if (request.PlannedArrivalUtc!.Value <= request.PlannedDepartureUtc!.Value)
        {
            errors[PlannedArrivalUtcField] =
                ["Planned arrival must be later than planned departure."];
        }
    }
}
