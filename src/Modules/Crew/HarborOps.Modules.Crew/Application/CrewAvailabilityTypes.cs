namespace HarborOps.Modules.Crew.Application;

internal static class CrewAvailabilityTypes
{
    internal const string AnnualLeave = "AnnualLeave";
    internal const string SickLeave = "SickLeave";
    internal const string Training = "Training";
    internal const string OtherUnavailable = "OtherUnavailable";

    internal static readonly string[] All = [AnnualLeave, SickLeave, Training, OtherUnavailable];
}
