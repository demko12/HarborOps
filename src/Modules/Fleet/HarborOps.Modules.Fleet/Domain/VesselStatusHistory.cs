namespace HarborOps.Modules.Fleet.Domain;

internal sealed class VesselStatusHistory
{
    internal const int StatusMaxLength = 50;
    internal const int ChangedByMaxLength = 200;
    internal const int NotesMaxLength = 1000;

    private VesselStatusHistory()
    {
    }

    private VesselStatusHistory(
        Guid id,
        Guid vesselId,
        string status,
        string changedBy,
        string? notes,
        DateTime changedAtUtc)
    {
        Id = id;
        VesselId = vesselId;
        Status = status;
        ChangedBy = changedBy;
        Notes = notes;
        ChangedAtUtc = changedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid VesselId { get; private set; }

    public string Status { get; private set; } = null!;

    public string ChangedBy { get; private set; } = null!;

    public string? Notes { get; private set; }

    public DateTime ChangedAtUtc { get; private set; }

    internal static VesselStatusHistory Record(
        Guid vesselId,
        string status,
        string changedBy,
        string? notes = null)
    {
        return new VesselStatusHistory(
            Guid.NewGuid(),
            vesselId,
            status,
            changedBy,
            notes,
            DateTime.UtcNow);
    }
}
