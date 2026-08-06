using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class RegisterCargoBookingValidator
{
    internal const string BookingNumberField = "bookingNumber";
    internal const string CargoTypeField = "cargoType";
    internal const string DescriptionField = "description";
    internal const string IsHazardousField = "isHazardous";
    internal const string PackageCountField = "packageCount";
    internal const string VoyageIdField = "voyageId";
    internal const string WeightKgField = "weightKg";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        RegisterCargoBookingRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredLength(
            request.BookingNumber,
            CargoBooking.BookingNumberMaxLength,
            BookingNumberField,
            "Booking number",
            errors);

        ValidateVoyageId(request.VoyageId, errors);

        ValidateRequiredLength(
            request.Description,
            CargoBooking.DescriptionMaxLength,
            DescriptionField,
            "Description",
            errors);

        ValidateRequiredLength(
            request.CargoType,
            CargoBooking.CargoTypeMaxLength,
            CargoTypeField,
            "Cargo type",
            errors);

        ValidateWeightKg(request.WeightKg, errors);
        ValidatePackageCount(request.PackageCount, errors);

        if (request.IsHazardous is null)
        {
            errors[IsHazardousField] = ["Hazardous flag is required."];
        }

        return errors;
    }

    private static void ValidatePackageCount(
        int? packageCount,
        IDictionary<string, string[]> errors)
    {
        if (packageCount is null)
        {
            errors[PackageCountField] = ["Package count is required."];
            return;
        }

        if (packageCount <= 0)
        {
            errors[PackageCountField] = ["Package count must be greater than zero."];
        }
    }

    private static void ValidateVoyageId(
        Guid? voyageId,
        IDictionary<string, string[]> errors)
    {
        if (voyageId is null)
        {
            errors[VoyageIdField] = ["Voyage identifier is required."];
            return;
        }

        if (voyageId == Guid.Empty)
        {
            errors[VoyageIdField] = ["Voyage identifier must not be empty."];
        }
    }

    private static void ValidateWeightKg(
        decimal? weightKg,
        IDictionary<string, string[]> errors)
    {
        if (weightKg is null)
        {
            errors[WeightKgField] = ["Weight is required."];
            return;
        }

        if (weightKg <= 0)
        {
            errors[WeightKgField] = ["Weight must be greater than zero."];
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
