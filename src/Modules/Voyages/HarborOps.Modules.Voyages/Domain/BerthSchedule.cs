namespace HarborOps.Modules.Voyages.Domain;

internal sealed class BerthSchedule
{
    internal const int StatusMaxLength = 30;

    internal const string ReservedStatus = "Reserved";
    internal const string OccupiedStatus = "Occupied";
    internal const string ReleasedStatus = "Released";

    internal static readonly IReadOnlyList<string> ActiveStatuses =
    [
        ReservedStatus,
        OccupiedStatus,
    ];

    private BerthSchedule()
    {
    }

    private BerthSchedule(
        Guid id,
        Guid berthId,
        Guid voyageStopId,
        DateTime startTime,
        DateTime endTime,
        string status)
    {
        Id = id;
        BerthId = berthId;
        VoyageStopId = voyageStopId;
        StartTime = startTime;
        EndTime = endTime;
        Status = status;
    }

    public Guid Id { get; private set; }

    public Guid BerthId { get; private set; }

    public Guid VoyageStopId { get; private set; }

    public DateTime StartTime { get; private set; }

    public DateTime EndTime { get; private set; }

    public string Status { get; private set; } = null!;

    internal static BerthSchedule Create(
        Guid berthId,
        Guid voyageStopId,
        DateTime startTime,
        DateTime endTime)
    {
        return new BerthSchedule(
            Guid.NewGuid(),
            berthId,
            voyageStopId,
            startTime,
            endTime,
            ReservedStatus);
    }

    internal void MarkOccupied()
    {
        if (!string.Equals(Status, ReservedStatus, StringComparison.Ordinal))
        {
            throw new BerthScheduleNotReservedException(Id, Status);
        }

        Status = OccupiedStatus;
    }

    internal void Release()
    {
        if (string.Equals(Status, ReleasedStatus, StringComparison.Ordinal))
        {
            throw new BerthScheduleAlreadyReleasedException(Id);
        }

        Status = ReleasedStatus;
    }
}
