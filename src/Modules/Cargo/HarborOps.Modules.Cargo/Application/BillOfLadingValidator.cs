using System.Globalization;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class BillOfLadingValidator
{
    internal const string BillNumberField = "billNumber";
    internal const string ShipperNameField = "shipperName";
    internal const string ShipperAddressField = "shipperAddress";
    internal const string ConsigneeNameField = "consigneeName";
    internal const string ConsigneeAddressField = "consigneeAddress";
    internal const string NotifyPartyNameField = "notifyPartyName";
    internal const string NotifyPartyAddressField = "notifyPartyAddress";
    internal const string PlaceOfReceiptField = "placeOfReceipt";
    internal const string PortOfLoadingField = "portOfLoading";
    internal const string PortOfDischargeField = "portOfDischarge";
    internal const string PlaceOfDeliveryField = "placeOfDelivery";
    internal const string FreightTermsField = "freightTerms";
    internal const string CargoMarksField = "cargoMarks";
    internal const string CargoDescriptionField = "cargoDescription";
    internal const string PackageCountField = "packageCount";
    internal const string WeightKgField = "weightKg";
    internal const string IssuePlaceField = "issuePlace";
    internal const string RequestedIssueDateField = "requestedIssueDate";
    internal const string ContainerIdsField = "containerIds";
    internal const string ReasonField = "reason";

    internal const int MaximumPackageCount = 1_000_000;
    internal const decimal MaximumWeightKg = 1_000_000_000m;

    internal static IReadOnlyDictionary<string, string[]> ValidateDraft(
        SaveBillOfLadingDraftRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateBillNumber(request.BillNumber, errors);

        ValidateRequiredText(
            request.ShipperName,
            ShipperNameField,
            BillOfLading.PartyNameMaxLength,
            "A shipper name",
            errors);

        ValidateRequiredText(
            request.ShipperAddress,
            ShipperAddressField,
            BillOfLading.PartyAddressMaxLength,
            "A shipper address",
            errors);

        ValidateRequiredText(
            request.ConsigneeName,
            ConsigneeNameField,
            BillOfLading.PartyNameMaxLength,
            "A consignee name",
            errors);

        ValidateRequiredText(
            request.ConsigneeAddress,
            ConsigneeAddressField,
            BillOfLading.PartyAddressMaxLength,
            "A consignee address",
            errors);

        ValidateOptionalText(
            request.NotifyPartyName,
            NotifyPartyNameField,
            BillOfLading.PartyNameMaxLength,
            "A notify party name",
            errors);

        ValidateOptionalText(
            request.NotifyPartyAddress,
            NotifyPartyAddressField,
            BillOfLading.PartyAddressMaxLength,
            "A notify party address",
            errors);

        ValidateRequiredText(
            request.PlaceOfReceipt,
            PlaceOfReceiptField,
            BillOfLading.LocationMaxLength,
            "A place of receipt",
            errors);

        ValidateRequiredText(
            request.PortOfLoading,
            PortOfLoadingField,
            BillOfLading.LocationMaxLength,
            "A port of loading",
            errors);

        ValidateRequiredText(
            request.PortOfDischarge,
            PortOfDischargeField,
            BillOfLading.LocationMaxLength,
            "A port of discharge",
            errors);

        ValidateRequiredText(
            request.PlaceOfDelivery,
            PlaceOfDeliveryField,
            BillOfLading.LocationMaxLength,
            "A place of delivery",
            errors);

        ValidateFreightTerms(request.FreightTerms, errors);

        ValidateRequiredText(
            request.CargoMarks,
            CargoMarksField,
            BillOfLading.CargoMarksMaxLength,
            "Cargo marks",
            errors);

        ValidateRequiredText(
            request.CargoDescription,
            CargoDescriptionField,
            BillOfLading.CargoDescriptionMaxLength,
            "A cargo description",
            errors);

        ValidatePackageCount(request.PackageCount, errors);
        ValidateWeight(request.WeightKg, errors);

        ValidateRequiredText(
            request.IssuePlace,
            IssuePlaceField,
            BillOfLading.LocationMaxLength,
            "An issue place",
            errors);

        if (request.RequestedIssueDate is null)
        {
            errors[RequestedIssueDateField] = ["A requested issue date is required."];
        }

        ValidateContainerIds(request.ContainerIds, errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateTotalsAgainstBooking(
        SaveBillOfLadingDraftRequest request,
        int bookingPackageCount,
        decimal bookingWeightKg)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.PackageCount is { } packageCount && packageCount > bookingPackageCount)
        {
            errors[PackageCountField] =
                [$"Package count must not exceed the booking total of {bookingPackageCount.ToString(CultureInfo.InvariantCulture)}."];
        }

        if (request.WeightKg is { } weightKg && weightKg > bookingWeightKg)
        {
            errors[WeightKgField] =
                [$"Weight must not exceed the booking total of {bookingWeightKg.ToString("0.###", CultureInfo.InvariantCulture)} kg."];
        }

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateVoid(
        VoidBillOfLadingRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["A void reason is required."];

            return errors;
        }

        ValidateOptionalText(
            request.Reason,
            ReasonField,
            BillOfLading.VoidReasonMaxLength,
            "A void reason",
            errors);

        return errors;
    }

    internal static bool TryParseFreightTerms(string? freightTerms, out FreightTerms parsed)
    {
        return Enum.TryParse(freightTerms, ignoreCase: true, out parsed)
            && parsed is Domain.FreightTerms.Prepaid or Domain.FreightTerms.Collect;
    }

    private static void ValidateBillNumber(
        string? billNumber,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(billNumber))
        {
            errors[BillNumberField] = ["A bill number is required."];

            return;
        }

        var normalized = BillOfLading.NormalizeBillNumber(billNumber);

        if (normalized.Length > BillOfLading.BillNumberMaxLength)
        {
            errors[BillNumberField] =
                [$"Bill number must not exceed {BillOfLading.BillNumberMaxLength} characters."];

            return;
        }

        if (!normalized.All(character => char.IsAsciiLetterOrDigit(character) || character is '-'))
        {
            errors[BillNumberField] =
                ["Bill number may only contain letters, digits and hyphens."];
        }
    }

    private static void ValidateFreightTerms(
        string? freightTerms,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(freightTerms))
        {
            errors[FreightTermsField] = ["Freight terms are required."];

            return;
        }

        if (!TryParseFreightTerms(freightTerms, out _))
        {
            errors[FreightTermsField] =
                [$"Freight terms must be '{nameof(Domain.FreightTerms.Prepaid)}' or '{nameof(Domain.FreightTerms.Collect)}'."];
        }
    }

    private static void ValidatePackageCount(
        int? packageCount,
        IDictionary<string, string[]> errors)
    {
        if (packageCount is not { } count)
        {
            errors[PackageCountField] = ["A package count is required."];

            return;
        }

        if (count <= 0 || count > MaximumPackageCount)
        {
            errors[PackageCountField] =
                [$"Package count must be between 1 and {MaximumPackageCount}."];
        }
    }

    private static void ValidateWeight(decimal? weightKg, IDictionary<string, string[]> errors)
    {
        if (weightKg is not { } weight)
        {
            errors[WeightKgField] = ["A weight is required."];

            return;
        }

        if (weight <= 0m || weight > MaximumWeightKg)
        {
            errors[WeightKgField] =
                [$"Weight must be greater than zero and must not exceed {MaximumWeightKg} kg."];
        }
    }

    private static void ValidateContainerIds(
        IReadOnlyList<Guid>? containerIds,
        IDictionary<string, string[]> errors)
    {
        if (containerIds is null)
        {
            return;
        }

        if (containerIds.Any(containerId => containerId == Guid.Empty))
        {
            errors[ContainerIdsField] = ["A selected container is not valid."];

            return;
        }

        if (containerIds.Distinct().Count() != containerIds.Count)
        {
            errors[ContainerIdsField] = ["The same container cannot be selected twice."];
        }
    }

    private static void ValidateRequiredText(
        string? value,
        string field,
        int maxLength,
        string label,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{label} is required."];

            return;
        }

        ValidateOptionalText(value, field, maxLength, label, errors);
    }

    private static void ValidateOptionalText(
        string? value,
        string field,
        int maxLength,
        string label,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"{label} must not exceed {maxLength} characters."];
        }
    }
}
