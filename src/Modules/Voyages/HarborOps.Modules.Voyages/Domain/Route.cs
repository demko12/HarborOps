namespace HarborOps.Modules.Voyages.Domain;

internal sealed class Route
{
    internal const int DescriptionMaxLength = 1000;
    internal const int NameMaxLength = 200;

    private Route()
    {
    }

    private Route(
        Guid id,
        string name,
        decimal estimatedDistance,
        int estimatedDuration,
        string? description)
    {
        Id = id;
        Name = name;
        EstimatedDistance = estimatedDistance;
        EstimatedDuration = estimatedDuration;
        Description = description;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public decimal EstimatedDistance { get; private set; }

    public int EstimatedDuration { get; private set; }

    internal static Route Create(
        string name,
        decimal estimatedDistance,
        int estimatedDuration,
        string? description = null)
    {
        return new Route(
            Guid.NewGuid(),
            name,
            estimatedDistance,
            estimatedDuration,
            description);
    }
}
