namespace HarborOps.Modules.Compliance.Domain;

public sealed class PscInspection
{
    internal const int InspectingAuthorityMaxLength = 200;

    public int Id { get; private set; }

    public Guid VesselId { get; private set; }

    public Guid PortId { get; private set; }

    public DateOnly Date { get; private set; }

    public string InspectingAuthority { get; private set; } = string.Empty;

    public int DeficiencyCount { get; private set; }

    public bool VesselDetained { get; private set; }

    private PscInspection()
    {
    }

    public PscInspection(
        Guid vesselId,
        Guid portId,
        DateOnly date,
        string inspectingAuthority,
        int deficiencyCount,
        bool vesselDetained)
    {
        VesselId = vesselId;
        PortId = portId;
        Date = date;
        InspectingAuthority = inspectingAuthority;
        DeficiencyCount = deficiencyCount;
        VesselDetained = vesselDetained;
    }
}
