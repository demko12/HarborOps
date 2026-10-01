using System.Globalization;
using System.Text.Json;

namespace HarborOps.Modules.Fleet.Application;

internal sealed record AisObservation(string Imo, string Mmsi, AisPosition Position);

internal sealed class AisMessageProcessor(AisTrackingState state, TimeProvider timeProvider)
{
    private readonly Dictionary<long, string> imoByMmsi = [];

    internal string[] TrackedMmsis => imoByMmsi.Keys.Order().Select(mmsi => mmsi.ToString("D9", CultureInfo.InvariantCulture)).ToArray();

    internal void SetFleet(IEnumerable<(string Imo, string? Mmsi)> vessels)
    {
        imoByMmsi.Clear();
        foreach (var vessel in vessels)
        {
            if (vessel.Mmsi is { Length: 9 } && vessel.Mmsi.All(char.IsAsciiDigit)
                && long.TryParse(vessel.Mmsi, out var mmsi) && mmsi > 0) imoByMmsi[mmsi] = vessel.Imo;
        }
        state.Retain(imoByMmsi.Values.ToHashSet(StringComparer.Ordinal));
    }

    internal AisObservation? Process(ReadOnlyMemory<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out _)) throw new InvalidOperationException("AIS subscription rejected.");
        if (!root.TryGetProperty("MessageType", out var typeElement)) return null;
        var type = typeElement.GetString();
        if (type == "SubscriptionConfirmation") { state.SetStatus("Connected"); return null; }
        if (type is null || !root.TryGetProperty("Message", out var messages)
            || !messages.TryGetProperty(type, out var message)
            || !message.TryGetProperty("Valid", out var valid) || valid.ValueKind != JsonValueKind.True
            || !message.TryGetProperty("UserID", out var userId) || !userId.TryGetInt64(out var mmsi)) return null;

        if (type is not ("PositionReport" or "StandardClassBPositionReport" or "ExtendedClassBPositionReport")
            || !imoByMmsi.TryGetValue(mmsi, out var matchedImo)
            || !message.TryGetProperty("Latitude", out var latitudeElement) || !latitudeElement.TryGetDecimal(out var latitude)
            || !message.TryGetProperty("Longitude", out var longitudeElement) || !longitudeElement.TryGetDecimal(out var longitude)
            || latitude is < -90 or > 90 || longitude is < -180 or > 180) return null;

        // Use the upstream observation time; delayed AIS must never appear newly observed.
        if (!root.TryGetProperty("MetaData", out var metadata)
            || !metadata.TryGetProperty("time_utc", out var timestamp)
            || !DateTimeOffset.TryParse(timestamp.GetString()?.Replace(" UTC", "", StringComparison.Ordinal),
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var reportedAt)
            || reportedAt > timeProvider.GetUtcNow().AddMinutes(1)) return null;
        var position = new AisPosition(latitude, longitude, reportedAt.UtcDateTime);
        state.Record(matchedImo, position);
        return new AisObservation(matchedImo, mmsi.ToString("D9", CultureInfo.InvariantCulture), position);
    }
}
