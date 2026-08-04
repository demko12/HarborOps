namespace HarborOps.Modules.Fleet.Domain;

internal sealed class Fleet
{
    internal const int NameMaxLength = 200;
    internal const int DescriptionMaxLength = 1000;

    private Fleet()
    {
    }

    private Fleet(
        Guid id,
        string name,
        string description,
        DateTime createdUtc)
    {
        Id = id;
        Name = name;
        Description = description;
        CreatedAtUtc = createdUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public DateTime CreatedAtUtc { get; private set; }

    internal static Fleet Register(
        string name,
        string description)
    {
        return new Fleet(
            Guid.NewGuid(),
            name,
            description,
            DateTime.UtcNow);
    }
}
