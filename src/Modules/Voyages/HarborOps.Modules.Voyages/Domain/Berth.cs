namespace HarborOps.Modules.Voyages.Domain;

internal sealed class Berth
{
    internal const int NameMaxLength = 100;
    internal const int StatusMaxLength = 30;

    private Berth()
    {
    }

    private Berth(
        Guid id,
        Guid portId,
        string name,
        decimal length,
        decimal maxDraft,
        string status)
    {
        Id = id;
        PortId = portId;
        Name = name;
        Length = length;
        MaxDraft = maxDraft;
        Status = status;
    }

    public Guid Id { get; private set; }

    public Guid PortId { get; private set; }

    public string Name { get; private set; } = null!;

    public decimal Length { get; private set; }

    public decimal MaxDraft { get; private set; }

    public string Status { get; private set; } = null!;

    internal static Berth Create(
        Guid portId,
        string name,
        decimal length,
        decimal maxDraft,
        string status)
    {
        return new Berth(
            Guid.NewGuid(),
            portId,
            name,
            length,
            maxDraft,
            status);
    }
}
