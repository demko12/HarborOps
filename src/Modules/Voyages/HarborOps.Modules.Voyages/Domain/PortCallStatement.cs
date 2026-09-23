using System.Globalization;

namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallStatement
{
    internal const int StatusMaxLength = 30;
    internal const int SourceVersionMaxLength = 64;
    internal const int FinalizedByNameMaxLength = PortCallActivityRevision.ActorNameMaxLength;
    internal const int PortTimeZoneMaxLength = Port.TimeZoneMaxLength;

    internal const string DraftStatus = "Draft";
    internal const string FinalizedStatus = "Finalized";

    private PortCallStatement()
    {
    }

    private PortCallStatement(
        Guid id,
        Guid voyageId,
        Guid voyageStopId,
        Guid createdByUserId,
        DateTime createdAtUtc)
    {
        Id = id;
        VoyageId = voyageId;
        VoyageStopId = voyageStopId;
        Status = DraftStatus;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid VoyageId { get; private set; }

    public Guid VoyageStopId { get; private set; }

    public string Status { get; private set; } = null!;

    public Guid CreatedByUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? FinalizedByUserId { get; private set; }

    public string? FinalizedByName { get; private set; }

    public DateTime? FinalizedAtUtc { get; private set; }

    public string? VoyageNumber { get; private set; }

    public int? StopOrder { get; private set; }

    public string? PortName { get; private set; }

    public string? PortUnLocode { get; private set; }

    public string? PortTimeZone { get; private set; }

    public string? BerthName { get; private set; }

    public DateTime? ActualArrivalUtc { get; private set; }

    public DateTime? ActualDepartureUtc { get; private set; }

    public string? SourceVersion { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    internal bool IsFinalized =>
        string.Equals(Status, FinalizedStatus, StringComparison.Ordinal);

    internal static string FormatDocumentNumber(string voyageNumber, int stopOrder) =>
        string.Create(CultureInfo.InvariantCulture, $"SOF-{voyageNumber}-{stopOrder:D2}");

    internal static PortCallStatement CreateDraft(
        Guid voyageId,
        Guid voyageStopId,
        Guid actorUserId,
        DateTime createdAtUtc)
    {
        return new PortCallStatement(
            Guid.NewGuid(),
            voyageId,
            voyageStopId,
            actorUserId,
            createdAtUtc);
    }

    internal IReadOnlyList<PortCallStatementEntry> Finalize(
        PortCallStatementSnapshot snapshot,
        Guid actorUserId,
        string actorName,
        DateTime finalizedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);

        EnsureDraft();

        VoyageNumber = snapshot.VoyageNumber;
        StopOrder = snapshot.StopOrder;
        PortName = snapshot.PortName;
        PortUnLocode = snapshot.PortUnLocode;
        PortTimeZone = snapshot.PortTimeZone;
        BerthName = snapshot.BerthName;
        ActualArrivalUtc = snapshot.ActualArrivalUtc;
        ActualDepartureUtc = snapshot.ActualDepartureUtc;
        SourceVersion = snapshot.SourceVersion;
        Status = FinalizedStatus;
        FinalizedByUserId = actorUserId;
        FinalizedByName = actorName;
        FinalizedAtUtc = finalizedAtUtc;
        ConcurrencyToken = Guid.NewGuid();

        return snapshot.Entries
            .Select((entry, index) => PortCallStatementEntry.Capture(Id, index + 1, entry))
            .ToList();
    }

    internal void EnsureDraft()
    {
        if (IsFinalized)
        {
            throw new PortCallStatementAlreadyFinalizedException(Id);
        }
    }
}
