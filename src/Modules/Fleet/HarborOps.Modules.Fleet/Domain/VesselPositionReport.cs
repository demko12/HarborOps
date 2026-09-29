namespace HarborOps.Modules.Fleet.Domain;

internal sealed class VesselPositionReport
{
    private VesselPositionReport() { }
    private VesselPositionReport(Guid vesselId, decimal latitude, decimal longitude, Guid reportedByUserId, DateTime reportedAtUtc, long sequence)
    { Id = Guid.NewGuid(); VesselId = vesselId; Latitude = latitude; Longitude = longitude; ReportedByUserId = reportedByUserId; ReportedAtUtc = reportedAtUtc; Sequence = sequence; }
    public Guid Id { get; private set; }
    public Guid VesselId { get; private set; }
    public decimal Latitude { get; private set; }
    public decimal Longitude { get; private set; }
    public Guid ReportedByUserId { get; private set; }
    public DateTime ReportedAtUtc { get; private set; }
    public long Sequence { get; private set; }
    internal static VesselPositionReport Create(Guid vesselId, decimal latitude, decimal longitude, Guid reportedByUserId, DateTime reportedAtUtc, long sequence) => new(vesselId, latitude, longitude, reportedByUserId, reportedAtUtc, sequence);
}
