using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal static class RequestCrewAvailabilityValidator
{
    internal const string TypeField = "type";
    internal const string StartDateField = "startDate";
    internal const string EndDateField = "endDate";
    internal const string ReasonField = "reason";
    internal const string NotesField = "notes";

    internal static IReadOnlyDictionary<string, string[]> Validate(CreateCrewAvailabilityRequestRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            errors[TypeField] = ["Unavailability type is required."];
        }
        else if (!CrewAvailabilityTypes.All.Contains(request.Type.Trim()))
        {
            errors[TypeField] = ["Unavailability type is not supported."];
        }

        ValidateDates(request.StartDate, request.EndDate, errors);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["Reason is required."];
        }
        else if (request.Reason.Trim().Length > CrewAvailabilityRequest.ReasonMaxLength)
        {
            errors[ReasonField] = [$"Reason must not exceed {CrewAvailabilityRequest.ReasonMaxLength} characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.Notes)
            && request.Notes.Trim().Length > CrewAvailabilityRequest.NotesMaxLength)
        {
            errors[NotesField] = [$"Notes must not exceed {CrewAvailabilityRequest.NotesMaxLength} characters."];
        }

        return errors;
    }

    private static void ValidateDates(DateOnly? startDate, DateOnly? endDate, IDictionary<string, string[]> errors)
    {
        if (startDate is null)
        {
            errors[StartDateField] = ["Start date is required."];
        }

        if (endDate is null)
        {
            errors[EndDateField] = ["End date is required."];
            return;
        }

        if (startDate is not null && endDate.Value < startDate.Value)
        {
            errors[EndDateField] = ["End date must be on or after the start date."];
        }
    }
}
