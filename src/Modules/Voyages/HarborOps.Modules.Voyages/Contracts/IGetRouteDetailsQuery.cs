namespace HarborOps.Modules.Voyages.Contracts;

public interface IGetRouteDetailsQuery
{
    Task<RouteDetailsResponse> GetRouteDetailsAsync(
        Guid routeId,
        CancellationToken cancellationToken);
}
