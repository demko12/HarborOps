using System.Text.Json;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal sealed record VesselManningPlanLineSnapshot(Guid RankId, int RequiredHeadcount);

internal static class VesselManningPlanLineSnapshotSerializer
{
    internal static string Serialize(IEnumerable<VesselManningPlanLine> lines)
    {
        var snapshot = lines
            .Select(line => new VesselManningPlanLineSnapshot(line.RankId, line.RequiredHeadcount))
            .OrderBy(line => line.RankId)
            .ToList();

        return JsonSerializer.Serialize(snapshot);
    }
}
