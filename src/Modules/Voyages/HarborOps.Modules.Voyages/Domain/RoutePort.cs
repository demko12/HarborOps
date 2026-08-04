namespace HarborOps.Modules.Voyages.Domain;

internal sealed class RoutePort
{
    private RoutePort()
    {
    }

    private RoutePort(
        Guid id,
        Guid routeId,
        Guid portId,
        int stopOrder,
        decimal distanceFromPrevious,
        int estimatedDurationFromPrevious)
    {
        Id = id;
        RouteId = routeId;
        PortId = portId;
        StopOrder = stopOrder;
        DistanceFromPrevious = distanceFromPrevious;
        EstimatedDurationFromPrevious = estimatedDurationFromPrevious;
    }

    public Guid Id { get; private set; }

    public Guid RouteId { get; private set; }

    public Guid PortId { get; private set; }

    public int StopOrder { get; private set; }

    public decimal DistanceFromPrevious { get; private set; }

    public int EstimatedDurationFromPrevious { get; private set; }

    internal static RoutePort Create(
        Guid routeId,
        Guid portId,
        int stopOrder,
        decimal distanceFromPrevious,
        int estimatedDurationFromPrevious)
    {
        return new RoutePort(
            Guid.NewGuid(),
            routeId,
            portId,
            stopOrder,
            distanceFromPrevious,
            estimatedDurationFromPrevious);
    }
}
