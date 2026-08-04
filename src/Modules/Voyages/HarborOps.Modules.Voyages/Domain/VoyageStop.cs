namespace HarborOps.Modules.Voyages.Domain;

internal sealed class VoyageStop
{
    internal const int StatusMaxLength = 30;

    private VoyageStop()
    {
    }

    private VoyageStop(
        Guid id,
        Guid voyageId,
        Guid portId,
        int stopOrder,
        DateTime eta,
        DateTime etd,
        string status,
        Guid? berthId)
    {
        Id = id;
        VoyageId = voyageId;
        PortId = portId;
        StopOrder = stopOrder;
        Eta = eta;
        Etd = etd;
        Status = status;
        BerthId = berthId;
    }

    public Guid Id { get; private set; }

    public Guid VoyageId { get; private set; }

    public Guid PortId { get; private set; }

    public Guid? BerthId { get; private set; }

    public int StopOrder { get; private set; }

    public DateTime Eta { get; private set; }

    public DateTime Etd { get; private set; }

    public DateTime? Ata { get; private set; }

    public DateTime? Atd { get; private set; }

    public string Status { get; private set; } = null!;

    internal static VoyageStop Create(
        Guid voyageId,
        Guid portId,
        int stopOrder,
        DateTime eta,
        DateTime etd,
        string status,
        Guid? berthId = null)
    {
        return new VoyageStop(
            Guid.NewGuid(),
            voyageId,
            portId,
            stopOrder,
            eta,
            etd,
            status,
            berthId);
    }
}
