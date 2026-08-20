using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;

namespace HarborOps.Modules.Fleet.Application;

internal static class RecordVesselStatusChangeValidator
{
    internal const string StatusField = "status";
    internal const string NotesField = "notes";

    internal static IReadOnlyDictionary<string, string[]> Validate(RecordVesselStatusChangeRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Status))
        {
            errors[StatusField] = ["Status is required."];
        }
        else if (!VesselStatus.IsValid(request.Status.Trim()))
        {
            errors[StatusField] =
                [$"Status must be one of: {string.Join(", ", VesselStatus.All)}."];
        }

        if (request.Notes is { Length: > 0 } notes
            && notes.Trim().Length > VesselStatusHistory.NotesMaxLength)
        {
            errors[NotesField] =
                [$"Notes must not exceed {VesselStatusHistory.NotesMaxLength} characters."];
        }

        return errors;
    }
}
