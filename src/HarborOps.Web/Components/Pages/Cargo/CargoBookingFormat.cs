using System.Globalization;

namespace HarborOps.Web.Components.Pages.Cargo;


internal static class CargoBookingFormat
{
    internal const string HazardousLabel = "Hazardous";
    internal const string NotHazardousLabel = "Not hazardous";
    internal const string DraftStatus = "Draft";
    internal const string SubmittedStatus = "Submitted";
    internal const string ApprovedStatus = "Approved";
    internal const string RejectedStatus = "Rejected";

    internal static readonly IReadOnlyList<string> Statuses =
    [
        "Registered",
        "Planned",
        "Loaded",
        "InTransit",
        "Arrived",
        "Unloaded",
        "Completed",
    ];

    internal static string WeightKg(decimal weightKg)
    {
        return weightKg.ToString("N3", CultureInfo.InvariantCulture);
    }

    internal static string PackageCount(int packageCount)
    {
        return packageCount.ToString("N0", CultureInfo.InvariantCulture);
    }

    internal static string CreatedUtc(DateTime createdUtc)
    {
        return createdUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    internal static string CreatedUtcDate(DateTime createdUtc)
    {
        return createdUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal static string CreatedUtcTime(DateTime createdUtc)
    {
        return createdUtc.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    internal static string HazardLabel(bool isHazardous)
    {
        return isHazardous ? HazardousLabel : NotHazardousLabel;
    }

    internal static string ShortVoyage(Guid voyageId)
    {
        return ShortIdentifier(voyageId);
    }

    internal static string ShortUser(Guid responsibleUserId)
    {
        return ShortIdentifier(responsibleUserId);
    }

    private static string ShortIdentifier(Guid identifier)
    {
        var text = identifier.ToString("D", CultureInfo.InvariantCulture);

        return $"{text[..8]}…{text[^4..]}";
    }

    internal static string StatusModifier(string status)
    {
        if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return "done";
        }

        return LifecycleStep(status) > 1 ? "active" : "pending";
    }

    internal static int LifecycleStep(string status)
    {
        var index = Statuses
            .Select((name, position) => (name, position))
            .FirstOrDefault(entry => string.Equals(entry.name, status, StringComparison.OrdinalIgnoreCase))
            .position;

        return Statuses.Any(name => string.Equals(name, status, StringComparison.OrdinalIgnoreCase))
            ? index + 1
            : 0;
    }

    internal static string OperationStateModifier(string operationState)
    {
        if (string.Equals(operationState, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return "done";
        }

        return string.Equals(operationState, "InProgress", StringComparison.OrdinalIgnoreCase)
            ? "active"
            : "pending";
    }

    internal const string PendingControlState = "Pending";
    internal const string ReleasedControlState = "Released";
    internal const string HeldControlState = "Held";

    internal static string ControlStateLabel(string? state)
    {
        return state switch
        {
            ReleasedControlState => "Released",
            HeldControlState => "On hold",
            _ => "Awaiting release",
        };
    }

    internal static string ControlStateClass(string? state)
    {
        return state switch
        {
            ReleasedControlState => "control-status control-status-released",
            HeldControlState => "control-status control-status-held",
            _ => "control-status control-status-pending",
        };
    }

    internal static string ControlBlockedExplanation(string? blockedReason)
    {
        return blockedReason switch
        {
            null => "This container is ready to be released.",
            "the container is already released" => "This container is already released for loading.",
            "the container is held" => "This container is on hold and cannot be released.",
            "it has not been inspected yet" =>
                "This container has not been inspected yet, so it cannot be released.",
            "its latest inspection did not pass" =>
                "The latest inspection did not pass, so this container cannot be released.",
            "it has no intact seal" =>
                "This container has no intact seal, so it cannot be released.",
            _ => $"This container cannot be released because {blockedReason}.",
        };
    }

    internal static string ControlActionLabel(string action)
    {
        return action switch
        {
            "SealAssigned" => "Seal assigned",
            "SealBroken" => "Seal broken",
            "InspectionRecorded" => "Inspection recorded",
            "Released" => "Released",
            "Held" => "Put on hold",
            "HoldLifted" => "Hold lifted",
            _ => action,
        };
    }

    internal static string TemperatureCelsius(decimal temperatureCelsius)
    {
        return $"{temperatureCelsius.ToString("0.##", CultureInfo.InvariantCulture)} °C";
    }

    internal static string SafetyStatusLabel(string? reviewStatus)
    {
        return reviewStatus switch
        {
            DraftStatus => "Draft",
            SubmittedStatus => "Awaiting review",
            ApprovedStatus => "Approved",
            RejectedStatus => "Rejected",
            _ => "Not declared",
        };
    }

    internal static string SafetyStatusClass(string? reviewStatus)
    {
        return reviewStatus switch
        {
            SubmittedStatus => "safety-status safety-status-submitted",
            ApprovedStatus => "safety-status safety-status-approved",
            RejectedStatus => "safety-status safety-status-rejected",
            _ => "safety-status safety-status-draft",
        };
    }
}
