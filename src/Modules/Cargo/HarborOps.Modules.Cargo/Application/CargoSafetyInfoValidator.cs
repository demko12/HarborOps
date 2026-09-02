using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class CargoSafetyInfoValidator
{
    internal const string HandlingNotesField = "handlingNotes";
    internal const string ImdgClassField = "imdgClass";
    internal const string NotesField = "notes";
    internal const string StatusField = "status";
    internal const string StorageConditionsField = "storageConditions";
    internal const string UnNumberField = "unNumber";

    internal static IReadOnlyDictionary<string, string[]> ValidateUpdate(
        UpdateCargoSafetyInfoRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateImdgClass(request.ImdgClass, errors);
        ValidateUnNumber(request.UnNumber, errors);
        ValidateStorageConditions(request.StorageConditions, errors);
        ValidateHandlingNotes(request.HandlingNotes, errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateReview(
        ReviewCargoSafetyInfoRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateDecision(request.Status, errors);
        ValidateReviewNotes(request.Status, request.Notes, errors);

        return errors;
    }

    internal static bool TryParseDecision(string? status, out CargoSafetyReviewStatus decision)
    {
        return Enum.TryParse(status, ignoreCase: true, out decision)
            && decision is CargoSafetyReviewStatus.Approved or CargoSafetyReviewStatus.Rejected;
    }

    private static void ValidateImdgClass(string? imdgClass, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(imdgClass))
        {
            errors[ImdgClassField] = ["IMDG class is required."];
            return;
        }

        if (!CargoSafetyFormat.TryNormalizeImdgClass(imdgClass, out _))
        {
            errors[ImdgClassField] =
                ["IMDG class must be a class digit with an optional division and compatibility group, such as '3', '2.1' or '1.4S'."];
        }
    }

    private static void ValidateUnNumber(string? unNumber, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(unNumber))
        {
            errors[UnNumberField] = ["UN number is required."];
            return;
        }

        if (!CargoSafetyFormat.TryNormalizeUnNumber(unNumber, out _))
        {
            errors[UnNumberField] =
                [$"UN number must be {CargoSafetyFormat.UnNumberDigitCount} digits, optionally prefixed with 'UN'."];
        }
    }

    private static void ValidateStorageConditions(
        string? storageConditions,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(storageConditions))
        {
            errors[StorageConditionsField] = ["Storage conditions are required."];
            return;
        }

        if (storageConditions.Trim().Length > CargoSafetyInfo.StorageConditionsMaxLength)
        {
            errors[StorageConditionsField] =
                [$"Storage conditions must not exceed {CargoSafetyInfo.StorageConditionsMaxLength} characters."];
        }
    }

    private static void ValidateHandlingNotes(
        string? handlingNotes,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(handlingNotes))
        {
            return;
        }

        if (handlingNotes.Trim().Length > CargoSafetyInfo.HandlingNotesMaxLength)
        {
            errors[HandlingNotesField] =
                [$"Handling notes must not exceed {CargoSafetyInfo.HandlingNotesMaxLength} characters."];
        }
    }

    private static void ValidateDecision(string? status, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            errors[StatusField] = ["Status is required."];
            return;
        }

        if (!TryParseDecision(status, out _))
        {
            errors[StatusField] =
                [$"Status must be '{nameof(CargoSafetyReviewStatus.Approved)}' or '{nameof(CargoSafetyReviewStatus.Rejected)}'."];
        }
    }

    private static void ValidateReviewNotes(
        string? status,
        string? notes,
        IDictionary<string, string[]> errors)
    {
        var isRejection = TryParseDecision(status, out var decision)
            && decision is CargoSafetyReviewStatus.Rejected;

        if (isRejection && string.IsNullOrWhiteSpace(notes))
        {
            errors[NotesField] = ["Review notes are required when a declaration is rejected."];
            return;
        }

        if (!string.IsNullOrWhiteSpace(notes)
            && notes.Trim().Length > CargoSafetyInfo.ReviewNotesMaxLength)
        {
            errors[NotesField] =
                [$"Review notes must not exceed {CargoSafetyInfo.ReviewNotesMaxLength} characters."];
        }
    }
}
