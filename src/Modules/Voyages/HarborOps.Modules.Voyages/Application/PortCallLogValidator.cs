using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.Modules.Voyages.Application;

internal static class PortCallLogValidator
{
    internal const string ActivityTypeField = "activityType";
    internal const string OccurredAtField = "occurredAt";
    internal const string EndedAtField = "endedAt";
    internal const string NotesField = "notes";
    internal const string ReasonField = "reason";
    internal const string VersionField = "version";
    internal const string SourceVersionField = "sourceVersion";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 50;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> ValidateRecord(
        RecordPortCallActivityRequest request,
        DateTimeOffset utcNow)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateActivityValues(
            request.ActivityType,
            request.OccurredAt,
            request.EndedAt,
            request.Notes,
            utcNow,
            errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateCorrect(
        CorrectPortCallActivityRequest request,
        DateTimeOffset utcNow)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateActivityValues(
            request.ActivityType,
            request.OccurredAt,
            request.EndedAt,
            request.Notes,
            utcNow,
            errors);
        ValidateReason(request.Reason, "Correction reason", errors);
        ValidateVersion(request.Version, errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateVoid(
        VoidPortCallActivityRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateReason(request.Reason, "Void reason", errors);
        ValidateVersion(request.Version, errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateFinalize(
        FinalizePortCallStatementRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateVersion(request.Version, errors);

        if (string.IsNullOrWhiteSpace(request.SourceVersion))
        {
            errors[SourceVersionField] = ["Source version from the finalization preview is required."];
        }
        else if (request.SourceVersion.Trim().Length > PortCallStatement.SourceVersionMaxLength)
        {
            errors[SourceVersionField] =
                [$"Source version must not exceed {PortCallStatement.SourceVersionMaxLength} characters."];
        }

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidatePaging(
        GetPortCallActivitiesRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.PageNumber is { } pageNumber && pageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize is { } pageSize)
        {
            if (pageSize < 1)
            {
                errors[PageSizeField] = ["Page size must be 1 or greater."];
            }
            else if (pageSize > MaxPageSize)
            {
                errors[PageSizeField] = [$"Page size must not exceed {MaxPageSize}."];
            }
        }

        return errors;
    }

    internal static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateActivityValues(
        string? activityType,
        DateTimeOffset? occurredAt,
        DateTimeOffset? endedAt,
        string? notes,
        DateTimeOffset utcNow,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(activityType))
        {
            errors[ActivityTypeField] = ["Activity type is required."];
        }
        else if (!PortCallActivityType.IsValid(activityType.Trim()))
        {
            errors[ActivityTypeField] =
                [$"Activity type must be one of: {string.Join(", ", PortCallActivityType.All)}."];
        }

        if (occurredAt is null)
        {
            errors[OccurredAtField] = ["Occurrence time is required."];
        }
        else if (occurredAt.Value > utcNow)
        {
            errors[OccurredAtField] = ["Occurrence time must not be in the future."];
        }

        if (endedAt is { } end)
        {
            if (end > utcNow)
            {
                errors[EndedAtField] = ["End time must not be in the future."];
            }
            else if (occurredAt is { } start && end < start)
            {
                errors[EndedAtField] = ["End time must not precede the occurrence time."];
            }
        }

        if (!string.IsNullOrWhiteSpace(notes) && notes.Trim().Length > PortCallActivity.NotesMaxLength)
        {
            errors[NotesField] = [$"Notes must not exceed {PortCallActivity.NotesMaxLength} characters."];
        }
    }

    private static void ValidateReason(
        string? reason,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            errors[ReasonField] = [$"{displayName} is required."];
        }
        else if (reason.Trim().Length > PortCallActivityRevision.ReasonMaxLength)
        {
            errors[ReasonField] =
                [$"{displayName} must not exceed {PortCallActivityRevision.ReasonMaxLength} characters."];
        }
    }

    private static void ValidateVersion(Guid? version, IDictionary<string, string[]> errors)
    {
        if (version is null || version == Guid.Empty)
        {
            errors[VersionField] = ["Version of the record being changed is required."];
        }
    }
}
