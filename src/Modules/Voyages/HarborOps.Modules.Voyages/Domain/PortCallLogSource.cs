namespace HarborOps.Modules.Voyages.Domain;

internal static class PortCallLogSource
{
    internal const int MaxLength = 30;

    internal const string StopArrival = "StopArrival";
    internal const string StopDeparture = "StopDeparture";
    internal const string ManualActivity = "ManualActivity";
    internal const string Delay = "Delay";

    internal static int Rank(string source) => source switch
    {
        StopArrival => 0,
        ManualActivity => 1,
        Delay => 2,
        StopDeparture => 3,
        _ => 4,
    };
}
