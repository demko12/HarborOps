namespace HarborOps.Modules.Crew.Application;

internal static class CrewEmploymentStatuses
{
    internal const string Active = "Active";
    internal const string OnLeave = "OnLeave";
    internal const string Suspended = "Suspended";
    internal const string Terminated = "Terminated";

    internal static readonly string[] All = [Active, OnLeave, Suspended, Terminated];
}
