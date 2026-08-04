namespace HarborOps.Modules.Voyages.Domain;

internal sealed class BerthSchedule
{
    internal const int StatusMaxLength = 30;

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
        DateTime endTime,
        string status)
    {
        return new BerthSchedule(
            Guid.NewGuid(),
            berthId,
            voyageStopId,
            startTime,
            endTime,
            status);
    }
}
