namespace HarborOps.Modules.Fleet.Domain;

internal sealed class VesselType
{
    internal const int NameMaxLength = 100;
    internal const int CodeMaxLength = 20;
    internal const int DescriptionMaxLength = 500;

    private VesselType()
    {
    }

    private VesselType(
        Guid id,
        string name,
        string code,
        string description)
    {
        Id = id;
        Name = name;
        Code = code;
        Description = description;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Code { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    internal static VesselType Register(
        string name,
        string code,
        string description)
    {
        return new VesselType(
            Guid.NewGuid(),
            name,
            code,
            description);
    }
}
