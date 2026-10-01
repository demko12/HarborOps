using HarborOps.Modules.Fleet.Contracts;

namespace HarborOps.Modules.Fleet.Application;

internal sealed record AisPosition(decimal Latitude, decimal Longitude, DateTime ReportedAtUtc);

internal sealed class AisTrackingState
{
    private readonly object gate = new();
    private readonly Dictionary<string, AisPosition> positions = new(StringComparer.Ordinal);
    private AisTrackingStatus status = new("Disabled", null);

    internal AisTrackingStatus Status { get { lock (gate) return status; } }

    internal void SetStatus(string connectionState)
    {
        lock (gate) status = status with { ConnectionState = connectionState };
    }

    internal void Record(string imo, AisPosition position)
    {
        lock (gate)
        {
            if (positions.TryGetValue(imo, out var previous) && previous.ReportedAtUtc >= position.ReportedAtUtc) return;
            positions[imo] = position;
            status = status with
            {
                ConnectionState = "Connected",
                LastPositionReceivedAtUtc = status.LastPositionReceivedAtUtc is { } last && last > position.ReportedAtUtc
                    ? last : position.ReportedAtUtc,
            };
        }
    }

    internal IReadOnlyDictionary<string, AisPosition> Snapshot()
    {
        lock (gate) return new Dictionary<string, AisPosition>(positions, StringComparer.Ordinal);
    }

    internal void Retain(IReadOnlySet<string> imos)
    {
        lock (gate)
        {
            foreach (var imo in positions.Keys.Where(imo => !imos.Contains(imo)).ToArray()) positions.Remove(imo);
        }
    }
}
