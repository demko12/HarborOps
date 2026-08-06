using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.Modules.Voyages.Application;

internal static class ScheduleVoyageValidator
{
    internal const string ArrivalPortCodeField = "arrivalPortCode";
    internal const string DeparturePortCodeField = "departurePortCode";
    internal const string PlannedArrivalUtcField = "plannedArrivalUtc";
    internal const string PlannedDepartureUtcField = "plannedDepartureUtc";
    internal const string VesselIdField = "vesselId";
    internal const string VoyageNumberField = "voyageNumber";

    internal static IReadOnlyDictionary<string, string[]> Validate(ScheduleVoyageRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredLength(
            request.VoyageNumber,
            Voyage.VoyageNumberMaxLength,
            VoyageNumberField,
            "Voyage number",
            errors);

        ValidateRequiredGuid(
            request.VesselId,
            VesselIdField,
            "Vessel",
            errors);

        ValidatePortCode(
            request.DeparturePortCode,
            DeparturePortCodeField,
            "Departure port code",
            errors);

        ValidatePortCode(
            request.ArrivalPortCode,
            ArrivalPortCodeField,
            "Arrival port code",
            errors);

        ValidateDifferentPortCodes(request, errors);

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

        return errors;
    }

    internal static string NormalizePortCode(string portCode)
    {
        return portCode.Trim().ToUpperInvariant();
    }

    private static void ValidatePortCode(
        string? value,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{displayName} is required."];
            return;
        }

        if (value.Trim().Length != Voyage.PortCodeLength)
        {
            errors[field] =
                [$"{displayName} must contain exactly {Voyage.PortCodeLength} characters."];
        }
    }

    private static void ValidateDifferentPortCodes(
        ScheduleVoyageRequest request,
        IDictionary<string, string[]> errors)
    {
        if (errors.ContainsKey(DeparturePortCodeField) || errors.ContainsKey(ArrivalPortCodeField))
        {
            return;
        }

        var departurePortCode = NormalizePortCode(request.DeparturePortCode!);
        var arrivalPortCode = NormalizePortCode(request.ArrivalPortCode!);

        if (string.Equals(departurePortCode, arrivalPortCode, StringComparison.Ordinal))
        {
            errors[ArrivalPortCodeField] =
                ["Departure and arrival port codes must be different."];
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
        ScheduleVoyageRequest request,
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

    private static void ValidateRequiredGuid(
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

    private static void ValidateRequiredLength(
        string? value,
        int maxLength,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{displayName} is required."];
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"{displayName} must not exceed {maxLength} characters."];
        }
    }
}
