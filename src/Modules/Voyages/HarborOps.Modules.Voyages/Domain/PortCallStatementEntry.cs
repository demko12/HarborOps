namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallStatementEntry
{
    internal const int LabelMaxLength = 200;

    private PortCallStatementEntry()
    {
    }

    private PortCallStatementEntry(
        Guid id,
        Guid portCallStatementId,
        int position,
        PortCallLogEntry entry)
    {
        Id = id;
        PortCallStatementId = portCallStatementId;
        Position = position;
        Source = entry.Source;
        SourceId = entry.SourceId;
        SourceRevision = entry.SourceRevision;
        Label = entry.Label;
        OccurredAtUtc = entry.OccurredAtUtc;
        EndedAtUtc = entry.EndedAtUtc;
        Notes = entry.Notes;
        DurationMinutes = entry.DurationMinutes;
    }

    public Guid Id { get; private set; }

    public Guid PortCallStatementId { get; private set; }

    public int Position { get; private set; }

    public string Source { get; private set; } = null!;

    public Guid? SourceId { get; private set; }

    public int? SourceRevision { get; private set; }

    public string Label { get; private set; } = null!;

    public DateTime OccurredAtUtc { get; private set; }

    public DateTime? EndedAtUtc { get; private set; }

    public string? Notes { get; private set; }

    public int? DurationMinutes { get; private set; }

    internal static PortCallStatementEntry Capture(
        Guid portCallStatementId,
        int position,
        PortCallLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new PortCallStatementEntry(Guid.NewGuid(), portCallStatementId, position, entry);
    }
}
