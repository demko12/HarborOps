namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallActivityChronologyException(DateTime occurredAtUtc, DateTime endedAtUtc)
    : Exception(
        $"A port-call activity cannot end at '{endedAtUtc:O}' before it starts at '{occurredAtUtc:O}'.")
{
    internal DateTime OccurredAtUtc { get; } = occurredAtUtc;

    internal DateTime EndedAtUtc { get; } = endedAtUtc;
}
