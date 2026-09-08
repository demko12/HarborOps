namespace HarborOps.Modules.Crew.Application;

internal static class CrewPositionSeniorityLevels
{
    internal const string Officer = "Officer";
    internal const string SeniorRating = "SeniorRating";
    internal const string JuniorRating = "JuniorRating";
    internal const string Cadet = "Cadet";

    internal static readonly string[] All = [Officer, SeniorRating, JuniorRating, Cadet];
}
