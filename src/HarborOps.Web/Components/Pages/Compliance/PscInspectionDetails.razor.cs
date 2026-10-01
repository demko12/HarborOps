using System.Globalization;
using HarborOps.Web.Api.Compliance;

namespace HarborOps.Web.Components.Pages.Compliance;

public partial class PscInspectionDetails
{
    internal const string StepDone = "done";
    internal const string StepCurrent = "current";
    internal const string StepPending = "pending";

    internal const string DetainedState = "Detained";
    internal const string ReleasedState = "Released";
    internal const string ClosedStatus = "Closed";
    internal const string InProgressStatus = "InProgress";

    internal static IReadOnlyList<ReleaseStep> ReleaseSteps(GetPscInspectionDetailResponse inspection, int overdueCount)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        var registeredDone = !inspection.HasDeficiencyCountMismatch;
        var closedDone = registeredDone && inspection.OpenDeficiencyCount == 0;

        var closedNote = string.Create(
            CultureInfo.InvariantCulture,
            $"{inspection.ClosedDeficiencyCount} of {inspection.RegisteredDeficiencyCount} closed");
        if (overdueCount > 0)
        {
            closedNote += string.Create(CultureInfo.InvariantCulture, $" · {overdueCount} overdue");
        }

        var release = inspection.DetentionState switch
        {
            ReleasedState => new ReleaseStep(4, "Released", ReleaseNote(inspection), StepDone),
            DetainedState => new ReleaseStep(
                4,
                "Released",
                closedDone ? "Ready for release" : "Blocked until all findings are closed",
                closedDone ? StepCurrent : StepPending),
            _ => new ReleaseStep(4, "Not detained", "No detention recorded", StepDone),
        };

        return
        [
            new ReleaseStep(1, "Inspected", inspection.InspectionDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture), StepDone),
            new ReleaseStep(
                2,
                "Deficiencies registered",
                string.Create(CultureInfo.InvariantCulture, $"{inspection.RegisteredDeficiencyCount} of {inspection.ReportedDeficiencyCount} reported"),
                registeredDone ? StepDone : StepCurrent),
            new ReleaseStep(3, "Corrective actions closed", closedNote, closedDone ? StepDone : registeredDone ? StepCurrent : StepPending),
            release,
        ];
    }

    private static string ReleaseNote(GetPscInspectionDetailResponse inspection)
    {
        var release = inspection.DetentionHistory.FirstOrDefault(decision => decision.Decision == ReleasedState);

        return release?.ReleaseDate is { } releaseDate
            ? releaseDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : "Vessel released";
    }

    internal static string DetentionStateClass(string detentionState)
    {
        return detentionState switch
        {
            DetainedState => "detained",
            ReleasedState => "released",
            _ => "clear",
        };
    }

    internal static string DetentionStateLabel(string detentionState)
    {
        return detentionState switch
        {
            DetainedState => "Detained",
            ReleasedState => "Released",
            _ => "Not detained",
        };
    }

    internal static string DetentionSummary(GetPscInspectionDetailResponse inspection, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        if (inspection.DetentionState == DetainedState && DetainedSince(inspection) is { } since)
        {
            var days = today.DayNumber - since.DayNumber;
            return days == 1 ? "1 day" : string.Create(CultureInfo.InvariantCulture, $"{days} days");
        }

        return DetentionStateLabel(inspection.DetentionState);
    }

    internal static string DetentionNote(GetPscInspectionDetailResponse inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        return inspection.DetentionState switch
        {
            DetainedState when DetainedSince(inspection) is { } since => $"Detained since {since.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}",
            ReleasedState => $"Released {ReleaseNote(inspection)}",
            _ => "The vessel was not detained",
        };
    }

    private static DateOnly? DetainedSince(GetPscInspectionDetailResponse inspection)
    {
        var detained = inspection.DetentionHistory.FirstOrDefault(decision => decision.Decision == DetainedState);

        return detained is null ? null : DateOnly.FromDateTime(detained.OccurredAtUtc.UtcDateTime);
    }

    internal static bool IsOverdue(PscDeficiencyItem item, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.ClosureStatus != ClosedStatus && item.CorrectiveActionDeadline < today;
    }

    internal static string DeficiencyStateClass(PscDeficiencyItem item, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.ClosureStatus == ClosedStatus)
        {
            return "closed";
        }

        return IsOverdue(item, today) ? "overdue" : "open";
    }

    internal static string DeficiencyStateLabel(PscDeficiencyItem item, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.ClosureStatus == ClosedStatus)
        {
            return "Closed";
        }

        if (IsOverdue(item, today))
        {
            return "Overdue";
        }

        return item.ClosureStatus == InProgressStatus ? "In progress" : "Open";
    }

    internal static int DeficiencyOrder(PscDeficiencyItem item, DateOnly today)
    {
        return DeficiencyStateClass(item, today) switch
        {
            "overdue" => 0,
            "open" => 1,
            _ => 2,
        };
    }

    internal static string DeficiencyIcon(PscDeficiencyItem item, DateOnly today)
    {
        return DeficiencyStateClass(item, today) switch
        {
            "closed" => "bi-check-lg",
            "overdue" => "bi-exclamation-triangle",
            _ => "bi-exclamation-circle",
        };
    }

    internal static string SeverityClass(string severity)
    {
        return severity switch
        {
            "Critical" => "critical",
            "Major" => "major",
            _ => "minor",
        };
    }

    internal static string DueLabel(PscDeficiencyItem item, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.ClosureStatus == ClosedStatus)
        {
            return string.Empty;
        }

        var days = item.CorrectiveActionDeadline.DayNumber - today.DayNumber;

        return days switch
        {
            < -1 => string.Create(CultureInfo.InvariantCulture, $"{-days} days overdue"),
            -1 => "1 day overdue",
            0 => "due today",
            1 => "due in 1 day",
            _ => string.Create(CultureInfo.InvariantCulture, $"due in {days} days"),
        };
    }

    internal static DeadlineAxisModel DeadlineAxis(DateOnly inspectionDate, IReadOnlyList<PscDeficiencyItem> deficiencies, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(deficiencies);

        var deadlines = deficiencies.Select(item => item.CorrectiveActionDeadline).ToList();
        var start = deadlines.Append(inspectionDate).Min();
        var last = deadlines.Append(today).Max();
        var dayCount = Math.Max(1, last.DayNumber - start.DayNumber + 1);
        var step = Math.Max(1, (int)Math.Ceiling(dayCount / 6d));

        var ticks = new List<DeadlineTick>();
        for (var offset = 0; offset < dayCount; offset += step)
        {
            var day = start.AddDays(offset);
            ticks.Add(new DeadlineTick(day.ToString("dd MMM", CultureInfo.InvariantCulture), offset * 100d / dayCount));
        }

        double? todayPercent = today >= start && today <= last
            ? (today.DayNumber - start.DayNumber + 0.5) * 100d / dayCount
            : null;

        return new DeadlineAxisModel(start, dayCount, ticks, todayPercent);
    }

    internal static string FormatPercent(double value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{value:0.###}%");
    }

    internal sealed record ReleaseStep(int Number, string Title, string Note, string State);

    internal sealed record DeadlineTick(string Label, double LeftPercent);

    internal sealed record DeadlineAxisModel(DateOnly Start, int DayCount, IReadOnlyList<DeadlineTick> Ticks, double? TodayPercent)
    {
        public double Percent(DateOnly day)
        {
            return Math.Clamp((day.DayNumber - Start.DayNumber + 0.5) * 100d / DayCount, 0, 100);
        }
    }
}
