using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.Modules.Compliance.Application;

internal static class PscDetentionState
{
    internal const string NotDetained = "NotDetained";
    internal const string Detained = "Detained";
    internal const string Released = "Released";

    internal static string FromLatestDecision(string? latestDecision, bool legacyVesselDetained) => latestDecision switch
    {
        PscDetentionDecision.DetainedDecision => Detained,
        PscDetentionDecision.ReleasedDecision => Released,
        _ when legacyVesselDetained => Detained,
        _ => NotDetained,
    };
}
